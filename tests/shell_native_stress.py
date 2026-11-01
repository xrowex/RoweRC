"""Explicit local-only shell/GC regression fixture. Requires an idle native game."""
import os,pathlib,time,json,subprocess
bridge=pathlib.Path(os.environ['LOCALAPPDATA'])/'RoweRC/bridge'
if 'role\tidle\n' not in (bridge/'ui-state.tsv').read_text(encoding='utf-8'):raise RuntimeError('Leave the real lobby first')
out=pathlib.Path('evidence/shells-native');out.mkdir(parents=True,exist_ok=True)
report=[]
def alive():
    return 'RCDriftSim-Win64-Shipping.exe' in subprocess.check_output(['tasklist','/FI','IMAGENAME eq RCDriftSim-Win64-Shipping.exe','/FO','CSV'],text=True)
for cycle in range(2):
    for selection in ('optional_shell','supra_rc','stock','gc','supra_rc','stock','gc','optional_shell','stock','gc'):
        if not alive():raise RuntimeError('Game exited before '+selection)
        serial=str(time.time_ns()//1000000)
        action='collect-garbage' if selection=='gc' else 'shell'
        result=bridge/('gc-result.txt' if selection=='gc' else 'shell-result.txt')
        (bridge/'command.tsv').write_bytes((serial+'\t'+action+'\t'+selection+'\t'+serial+'\n').encode())
        end=time.monotonic()+10
        while time.monotonic()<end:
            if not alive():raise RuntimeError('Game exited during '+selection)
            try:text=result.read_text(encoding='utf-8')
            except OSError:text=''
            if text.startswith(serial+'\t'):
                if selection!='gc' and text!=serial+'\tApplied '+selection:raise RuntimeError(text)
                break
            time.sleep(.15)
        else:raise RuntimeError('No native result for '+selection)
        time.sleep(1.2 if selection!='gc' else 2)
        if not alive():raise RuntimeError('Game exited after '+selection)
        snapshot=(bridge/'appearance.tsv').read_text()
        body=next(l for l in snapshot.splitlines() if l.startswith('p\t1\tOptionalBodyShell\t'))
        if selection not in ('gc','stock') and '/Shells/'+selection+'/' not in body:raise RuntimeError('Wrong native body: '+body)
        if selection=='stock' and '/Shells/' in body:raise RuntimeError('Stock restore did not finish')
        report.append({'cycle':cycle+1,'selection':selection,'result':text,'body':body})
        (out/'stress-report.json').write_text(json.dumps({'completed':False,'steps':report},indent=2)+'\n')
        print('PASS: cycle '+str(cycle+1)+' / '+selection,flush=True)
(out/'stress-report.json').write_text(json.dumps({'completed':True,'shellChanges':14,'forcedCollections':6,'steps':report},indent=2)+'\n')
print('PASS: 14 native shell changes and 6 forced collections; game stayed alive',flush=True)

"""Explicit offline fixture: read back real deformed vertices and ResetCar repair."""
import json,os,pathlib,re,time,subprocess
bridge=pathlib.Path(os.environ['LOCALAPPDATA'])/'RoweRC/bridge'
if 'role\tidle\n' not in (bridge/'ui-state.tsv').read_text(encoding='utf-8'):raise RuntimeError('Leave the real lobby before testing')
out=pathlib.Path('evidence/shells-native');report=[]
def wait(predicate):
    until=time.monotonic()+25
    while time.monotonic()<until:
        if 'RCDriftSim-Win64-Shipping.exe' not in subprocess.check_output(['tasklist','/FI','IMAGENAME eq RCDriftSim-Win64-Shipping.exe','/FO','CSV'],text=True):raise RuntimeError('Game exited')
        try:
            value=predicate()
            if value:return value
        except OSError:pass
        time.sleep(.15)
    raise RuntimeError('Native damage fixture timed out')
def request(action,arg='',result=None):
    serial=str(time.time_ns()//1000000)
    (bridge/'command.tsv').write_bytes((serial+'\t'+action+'\t'+arg+'\t'+serial+'\n').encode())
    if result:
        def read():
            text=(bridge/result).read_text()
            return text if text.startswith(serial+'\t') else None
        return wait(read)
for key in ('optional_shell','supra_rc'):
    response=request('shell',key,'shell-result.txt');assert response.endswith('Applied '+key),response
    time.sleep(.5)
    request('dent-test')
    def dented():
        fields=(bridge/'damage.tsv').read_text().rstrip('\n').split('\t')
        return fields if len(fields)==5 and '/'+key+'/' in fields[3] and fields[4] else None
    damage=wait(dented)
    verified=request('dent-verify',result='dent-verify.txt')
    moved=int(re.search(r'moved=(\d+)',verified)[1]);depth=float(re.search(r'depth=([\d.]+)',verified)[1])
    assert moved>0 and .01<depth<=1.2001 and 'visible=true' in verified,verified
    assert not (bridge/'damage-error.txt').read_text().strip()
    request('reset')
    time.sleep(1)
    repaired=request('dent-verify',result='dent-verify.txt')
    assert '\trepaired\tvisible=true' in repaired,repaired
    fields=(bridge/'damage.tsv').read_text().rstrip('\n').split('\t');assert fields[4]==''
    revision=fields[2];time.sleep(.5)
    assert (bridge/'damage.tsv').read_text().split('\t')[2]==revision,'Damage revision must stabilize after repair'
    report.append({'shell':key,'damage':damage,'nativeReadback':verified,'repair':repaired,'moved':moved,'maxDepthCm':depth})
    (out/'deformation-verification.json').write_text(json.dumps({'completed':False,'shells':report},indent=2)+'\n')
    print('PASS: '+key+' / native moved='+str(moved)+' / depth='+str(depth)+' cm / ResetCar repaired',flush=True)
(out/'deformation-verification.json').write_text(json.dumps({'completed':True,'shells':report},indent=2)+'\n')

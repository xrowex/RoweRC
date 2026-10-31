"""Local native rendering fixture. Not a Steam peer or a two-PC acceptance test."""
import hashlib,math,os,pathlib,time,sys

bridge=pathlib.Path(os.environ['LOCALAPPDATA'])/'RoweRC/bridge'
state=(bridge/'ui-state.tsv').read_text(encoding='utf-8')
if 'role\tidle' not in state: raise RuntimeError('Leave the real lobby before running the local fixture')
original=(bridge/'appearance.tsv').read_text(encoding='utf-8')
peer='76561198000000001'
lines=original.splitlines()
red=[]
for line in lines:
    p=line.split('\t')
    if p[0]=='v' and p[3]=='BodyColor': p[4]='0.8,0.015,0.01,1'
    if p[0]=='p' and p[2].startswith('RimDisplay'): p[3]='/Game/Vehicle/Wheels/SM_Rim_Option2.SM_Rim_Option2'
    red.append('\t'.join(p))
snapshot='\n'.join(red)+'\n'
revision=hashlib.sha256(snapshot.encode()).hexdigest()
(bridge/('appearance.'+peer+'.tsv')).write_bytes(snapshot.encode())
try:
    for i in range(int(float(sys.argv[1] if len(sys.argv)>1 else 45)*20)):
        if i==240:
            snapshot=snapshot.replace('/Game/Vehicle/Bodies/Nissan350Z/SM_Nissan350Z_Chassis2_Body.SM_Nissan350Z_Chassis2_Body',
                '/Game/Vehicle/Bodies/ToyotaSupra/SM_ToyotaSupra_Body.SM_ToyotaSupra_Body')
            rows=snapshot.splitlines(); h=rows[0].split('\t'); e=rows[-1].split('\t'); h[2]=e[2]=str(int(h[2])+1)
            rows[0]='\t'.join(h); rows[-1]='\t'.join(e); snapshot='\n'.join(rows)+'\n'
            revision=hashlib.sha256(snapshot.encode()).hexdigest()
            (bridge/('appearance.'+peer+'.tsv')).write_bytes(snapshot.encode())
        choices=[p for p in (bridge/'local.0.tsv',bridge/'local.1.tsv') if p.exists()]
        parts=max(choices,key=lambda p:p.stat().st_mtime).read_text().strip().split('\t')
        if len(parts)!=12: continue
        pose=list(map(float,parts[4:10])); pose[0]+=70+10*math.sin(i/30)
        motion=list(map(float,parts[10].split(',')))
        if len(motion)!=67: raise RuntimeError('New native animation frame is required')
        for wheel in range(4):
            offset=1+wheel*6
            motion[offset+3]=((i*12+180)%360)-180
            if wheel<2: motion[offset+4]=25*math.sin(i/25)
        motion[49:67]=[0,1,0]*6
        motion[52:55]=[.22,1+.2*math.sin(i/40),1]
        fields=[peer]+[str(v) for v in pose]+[parts[1],str(i),','.join(str(v) for v in motion),revision]
        pending=bridge/'remote-fixture.tmp'
        pending.write_bytes(('RRC1\n# fixture-%d\n'%i+'\t'.join(fields)+'\n').encode())
        try: pending.replace(bridge/'remote.tsv')
        except PermissionError: pass
        time.sleep(.05)
finally:
    (bridge/'remote.tsv').write_bytes(b'RRC1\n# fixture-ended\n')
print('Completed local paint/rim/wheel/audio fixture. No Steam traffic was tested.')

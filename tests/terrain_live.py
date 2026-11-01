"""Disconnected native-game regression. Enter city Drive view before running."""
import pathlib,time,json,math
bridge=pathlib.Path('C:/Users/xrowe/AppData/Local/RoweRC/bridge')
out=pathlib.Path('evidence/apex-map-import/native-support')
state=(bridge/'ui-state.tsv').read_text()
assert '\nrole\tidle\n' in state,'Disconnected test session required'
def frame():
    frames=[]
    for path in bridge.glob('local.[01].tsv'):
        try:
            p=path.read_text().strip().split('\t')
            if len(p)==13:frames.append(p)
        except OSError:pass
    p=max(frames,key=lambda f:int(f[2]));return {'seq':int(p[2]),'level':p[3],'pose':list(map(float,p[4:10])),'at':time.perf_counter()}
def command(name,spec=''):
    n=str(time.time_ns()//1000000);(bridge/'command.tsv').write_text(f'{n}\t{name}\t{spec}\n');return n
def probe(values):
    n=str(time.time_ns()//1000000);command('terrain-test',n+' '+' '.join(map(str,values)))
    until=time.monotonic()+3
    while time.monotonic()<until:
        result=(bridge/'terrain-probe-result.tsv').read_text()
        if result.startswith(n+'\tOK'):return
        if result.startswith('ERROR'):raise RuntimeError(result)
        time.sleep(.01)
    raise RuntimeError('Terrain probe timeout')
report={'boundary':[],'raisedRoad':None}
try:
    probe([-715.681611,-803.91078,4.5,180,0,0]);time.sleep(.1);raised=frame()
    assert raised['pose'][2]>4 and abs(raised['pose'][3])>2,raised
    report['raisedRoad']=raised
    time.sleep(1.1)
    camera=(bridge/'terrain-state.tsv').read_text().strip().split('\t')
    assert len(camera)>=14 and abs(float(camera[13])-float(camera[12])-float(camera[5]))<.001,'Camera must follow supported road height'
    report['camera']={'groundZ':float(camera[5]),'nativeZ':float(camera[12]),'adjustedZ':float(camera[13])}
    for name,values,axis,direction in [
        ('positiveX',[1795,266.7,0,0,5,0],0,1),
        ('negativeX',[-1795,294,0,180,-5,0],0,-1),
        ('positiveY',[0,1795,0,90,0,5],1,1),
        ('negativeY',[0,-1795,0,-90,0,-5],1,-1)]:
        probe(values);samples=[];until=time.monotonic()+1.5
        while time.monotonic()<until:samples.append(frame());time.sleep(.015)
        # A real city barrier may stop the car just beyond the old limit.
        passed=max(f['pose'][axis]*direction for f in samples)>1800.5
        fields=(bridge/'terrain-state.tsv').read_text().strip().split('\t');driving=fields[2]=='1'
        item={'name':name,'crossedBoundary':passed,'driving':driving,'samples':samples,'terrain':fields}
        report['boundary'].append(item);print(name,passed,driving,flush=True)
        assert passed and driving,{'name':name,'crossed':passed,'driving':driving}
    report['completed']=True
finally:
    command('reset');(out/'live-regression.json').write_text(json.dumps(report,indent=2))
print('PASS: live road height/banking and all four native arena limits',flush=True)

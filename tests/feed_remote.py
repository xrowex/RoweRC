import pathlib,time,math
b=pathlib.Path(r'C:\Users\xrowe\AppData\Local\RoweRC\bridge')
parts=(b/'local.1.tsv').read_text().strip().split('\t')
x,y,z,p,yaw,r=map(float,parts[4:10])
try:
    for i in range(600):
        text='RRC1\n# test-%d\n76561198000000001\t%f\t%f\t%f\t%f\t%f\t%f\n'%(i,x+70+15*math.sin(i/20),y,z,p,yaw,r)
        (b/'remote.tsv').write_bytes(text.encode())
        time.sleep(.1)
finally:
    (b/'remote.tsv').write_bytes(b'RRC1\n# test-complete\n')
print('Local rendering test feed completed; no Steam peer was connected.')

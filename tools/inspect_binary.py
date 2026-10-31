import re,json,pathlib
p=pathlib.Path(r'C:\Program Files\Epic Games\RCTandemszQS5H\RCDriftSim\Binaries\Win64\RCDriftSim-Win64-Shipping.exe')
b=p.read_bytes()
strings=re.findall(rb'[ -~]{6,}',b)+[x.decode('utf-16le').encode() for x in re.findall(rb'(?:[\x20-\x7e]\x00){6,}',b)]
patterns=[r'\+\+UE5',r'\b5\.[678]\.\d',r'RCDrift.*\.cpp',r'RC.*(Pawn|Controller|GameMode|GameInstance)',r'SteamAPI',r'(Server|Client|Multicast).*(Input|Vehicle|Transform)',r'Custom.*(Map|Track)',r'/Script/RCDriftSim']
matches=sorted(set(s.decode('ascii','replace') for s in strings if any(re.search(p,s.decode('ascii','replace')) for p in patterns)))
pathlib.Path('evidence/binary-strings.txt').write_text('\n'.join(matches))
print('\n'.join(matches[:130]))

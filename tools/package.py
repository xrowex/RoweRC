import hashlib,json,pathlib,zipfile

root=pathlib.Path(__file__).resolve().parents[1]
dist=root/'dist'
stage=dist/'RoweRC'
docs=['README.md','CUSTOM_MAPS.md','THIRD_PARTY.md','Install.ps1']
files=[p for sub in ['InstallFiles','licenses'] for p in (stage/sub).rglob('*') if p.is_file()]
files += [stage/name for name in docs]
manifest={
    'version':'0.1.1-experimental',
    'supportedGameBuild':'3c46a39f90',
    'engine':'5.7',
    'steamAppId':480,
    'inGameMenu':'F8',
    'tested':['loader startup','automatic hidden helper startup','in-game menu','Steam lobby creation and leave','built-in map travel','local simulated remote-car rendering','49 protocol/map/update checks'],
    'notVerified':['second-account join','two-PC car exchange','latency/tandem acceptance','newly cooked custom map','cosmetic/tune persistence across raw map travel'],
    'sha256':{p.relative_to(stage).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(files)}
}
(stage/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
files.append(stage/'manifest.json')
binary=dist/'RoweRC-prototype-0.1.1.zip'
with zipfile.ZipFile(binary,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for p in files: z.write(p,'RoweRC/'+p.relative_to(stage).as_posix())
source=dist/'RoweRC-source-0.1.1.zip'
source_files=[root/p for p in docs+['Build.ps1','Prepare-Update.ps1','.gitignore']]
source_files += [p for folder in ['src','mod','tests'] for p in (root/folder).rglob('*')
    if p.is_file() and not any(part in ['bin','obj','__pycache__'] for part in p.relative_to(root).parts)]
source_files += [root/'tools/package.py',root/'tools/inspect_binary.py']
with zipfile.ZipFile(source,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for p in source_files: z.write(p,'RoweRC-source/'+p.relative_to(root).as_posix())
for p in [binary,source]:
    with zipfile.ZipFile(p) as z:
        assert z.testzip() is None
    print(p.name, p.stat().st_size, hashlib.sha256(p.read_bytes()).hexdigest())

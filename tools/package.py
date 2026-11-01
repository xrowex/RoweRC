import hashlib,json,pathlib,zipfile,subprocess

root=pathlib.Path(__file__).resolve().parents[1]
dist=root/'dist'
stage=dist/'RoweRC'
docs=['README.md','CUSTOM_MAPS.md','CUSTOM_SHELLS.md','MAP_RELEASES.md','RELEASE_NOTES.md','THIRD_PARTY.md','Install.ps1','Install-Shells.ps1']
files=[p for sub in ['InstallFiles','licenses'] for p in (stage/sub).rglob('*') if p.is_file()]
files += [stage/name for name in docs]
manifest={
    'version':'0.1.8',
    'sourceCommit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip(),
    'supportedGameBuild':'1801ffbdfe',
    'engine':'5.7',
    'steamAppId':480,
    'inGameMenu':'F8',
    'tested':['105 backend checks','11 Lua collision checks','terrain layers and swept obstacle contacts','native city height and banking','absolute camera follows road height','all four native arena limits','stock Tandyz native tick restored','bundled terrain extraction and checksum restoration'],
    'notVerified':['full city driving lap','two-PC custom-shell visuals','two-PC physical contacts','high-latency contact feel','sustained two-PC smoothness'],
    'sha256':{p.relative_to(stage).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(files)}
}
(stage/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
files.append(stage/'manifest.json')
binary=dist/'RoweRC-prototype-0.1.8.zip'
with zipfile.ZipFile(binary,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for p in files: z.write(p,'RoweRC/'+p.relative_to(stage).as_posix())
source=dist/'RoweRC-source-0.1.8.zip'
source_files=[root/p for p in docs+['Build.ps1','Prepare-Update.ps1','.gitignore']]
source_files += [root/'ShellPacks/manifest.json']
source_files += [p for folder in ['src','mod','tests'] for p in (root/folder).rglob('*')
    if p.is_file() and not any(part in ['bin','obj','__pycache__'] for part in p.relative_to(root).parts)]
source_files += [root/'tools/package.py',root/'tools/inspect_binary.py',root/'tools/export_shell_collision.py',root/'tools/bake_apex_terrain.py',root/'tools/validate_legacy_update.ps1']
source_files += [root/'Maps/Support/apex-wangan.rrcterrain.gz']
with zipfile.ZipFile(source,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for p in source_files: z.write(p,'RoweRC-source/'+p.relative_to(root).as_posix())
for p in [binary,source]:
    with zipfile.ZipFile(p) as z:
        assert z.testzip() is None
    print(p.name, p.stat().st_size, hashlib.sha256(p.read_bytes()).hexdigest())

import sys,pathlib
sys.path.insert(0, str(pathlib.Path('tools/python').resolve()))
from lupa.lua54 import LuaRuntime
lua=LuaRuntime(unpack_returned_tuples=True)
for path in pathlib.Path('mod/RoweRC/Scripts').glob('*.lua'):
    text=path.read_text(encoding='utf-8')
    f,err=lua.eval('function(s) local f,e=load(s); return f,e end')(text)
    if f is None: raise RuntimeError(err)
    print('PASS: Lua 5.4 parses '+path.name)

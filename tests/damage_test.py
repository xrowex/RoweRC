import pathlib,sys
sys.path.insert(0,str(pathlib.Path('tools/python').resolve()))
from lupa.lua54 import LuaRuntime
lua=LuaRuntime(unpack_returned_tuples=True)
lua.execute("package.path='mod/RoweRC/Scripts/?.lua;'..package.path")
lua.execute(r'''
local M=require('damage');local checks=0
local function check(v,n)assert(v,n);checks=checks+1;print('PASS: '..n)end
local source={{X=1,Y=0,Z=0},{X=1,Y=6,Z=0},{X=1,Y=0,Z=3}}
local dent={x=1,y=0,z=0,nx=1,ny=0,depth=.6,radius=5}
local v=M.deform(source,{dent})
check(math.abs(v[1].X-.4)<1e-9 and v[2].X==1,'dent center moves inward and vertices outside its radius stay unchanged')
check(source[1].X==1 and source[3].Z==v[3].Z,'source geometry stays intact for repair and shell height is preserved')
local extreme=M.deform(source,{dent,dent,dent,dent})
check(math.abs(extreme[1].X-source[1].X)<=1.200001,'stacked dents stay inside the total displacement limit')
local reset=M.deform(source,{})
check(reset[1].X==source[1].X and reset[3].Y==source[3].Y,'an empty damage state exactly restores original positions')
check(#M.parse('1:0:.6;0:-1:.2')==2 and #M.parse('')==0,'valid dent snapshots and repair snapshots parse')
check(not pcall(M.parse,'0:0:.5') and not pcall(M.parse,'1:0:5') and not pcall(M.parse,'nan:0:.1'),'invalid directions and unsafe depths cannot reach geometry updates')
local writes={}
local meshName='First'
local mesh={IsValid=function()return true end,GetFullName=function()return 'StaticMesh /Game/Vehicle/RoweRC/Shells/'..meshName..'.'..meshName end}
local body={IsValid=function()return true end,StaticMesh=mesh}
local pawn={BodyShell=body}
local function write(name,text)writes[name]=text end
M.update(pawn,{},'test',write)
meshName='Second';M.update(pawn,{},'test',write)
local revision=writes['damage.tsv']:match('RRD1\ttest\t(%d+)')
for _=1,5 do M.update(pawn,{},'test',write) end
check(writes['damage.tsv']:match('RRD1\ttest\t(%d+)')==revision,'shell changes reset dents once and keep the next damage revision stable')
print(checks..' damage checks passed')
''')

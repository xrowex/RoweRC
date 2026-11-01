"""Exercise finish lifecycle without touching real saved user settings."""
import pathlib, sys
sys.path.insert(0, str(pathlib.Path('tools/python').resolve()))
from lupa.lua54 import LuaRuntime
lua = LuaRuntime(unpack_returned_tuples=True)
lua.execute("package.path='mod/RoweRC/Scripts/?.lua;'..package.path")
lua.execute('''
local saved
io.open=function(path,mode)
    if mode=='rb' then return nil end
    return {write=function(self,text)saved=text;return self end,close=function()end}
end
function FName(n)return n end
local function valid()return true end
local base={IsValid=valid,IsA=function()return false end,GetFullName=function()return 'Material /Game/Environment/Workbench/M_Workbench.M_Workbench' end}
local function mid(id,value)
    return {IsValid=valid,IsA=function(_,class)return class=='/Script/Engine.MaterialInstanceDynamic' end,
        Parent=base,value=value,color=id,roughness=.3,GetAddress=function()return id end,
        K2_GetScalarParameterValue=function(self,n)assert(n=='Metallic');return self.value end,
        SetScalarParameterValue=function(self,n,v)assert(n=='Metallic');self.value=v end}
end
local front,rear=mid(1,.2),mid(2,.4)
local function source(name,m)
    return {IsValid=valid,GetFullName=function()return 'StaticMeshComponent /Game/Maps/Test.Test:Pawn.'..name end,
        GetNumMaterials=function()return 1 end,GetMaterial=function()return m end}
end
local rims={source('RimDisplay1_FL',front),source('RimDisplay1_RR',rear),source('TireDisplay1_FL',mid(3,.1))}
local pawn={IsValid=valid}
local each=function(_,_,callback)for _,s in ipairs(rims)do callback(s) end end
local M=require('rims')
assert(M.update(pawn,each,0)==2 and front.value==.2 and rear.value==.4,'stock values untouched until selected')
M.set(.817);M.update(pawn,each,.2)
assert(front.value==.82 and rear.value==.82 and saved=='0.82\\n','slider rounds and persists on both axles')
assert(front.color==1 and rear.color==2 and front.roughness==.3,'preserve rim color and roughness')
assert(rims[3]:GetMaterial(0).value==.1,'never touch tires')
front.value=.2; M.update(pawn,each,.4)
assert(front.value==.82,'reapply after native customization updates')
M.stock(); assert(front.value==.2 and rear.value==.4 and saved=='stock\\n','restore native per-axle finish')
M.set(3); assert(M.value()==1,'clamp high'); M.set(-2);assert(M.value()==0,'clamp low')
assert(not pcall(M.set,0/0) and not pcall(M.set,math.huge),'reject nonfinite values')
M.set(.5);M.clear();front=mid(4,.1);rims={source('RimDisplay1_FL',front)}
M.update(pawn,each,0);assert(front.value==.5,'reapply after map travel')
M.stock();assert(front.value==.1,'restores new world defaults')
print('PASS: rim lifecycle, both axles, persistence, restore, travel and color/tire isolation')
''')

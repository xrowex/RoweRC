import pathlib,sys
sys.path.insert(0,str(pathlib.Path('tools/python').resolve()))
from lupa.lua54 import LuaRuntime
lua=LuaRuntime(unpack_returned_tuples=True)
lua.execute("package.path='mod/RoweRC/Scripts/?.lua;'..package.path")
lua.execute('''
local calls={}
local mesh='/Game/Vehicle/Bodies/Nissan180SX/SM_Nissan180SX_Body.SM_Nissan180SX_Body'
local paint='/Game/Vehicle/Customization/Materials/M_Paint_Nissan180SX.M_Paint_Nissan180SX'
local function object(class) return {IsValid=function()return true end,IsA=function(_,c)return c==class end} end
function StaticFindObject() return {IsValid=function()return false end} end
function LoadAsset(path)
    calls[#calls+1]=path
    if path==mesh then return object('/Script/Engine.StaticMesh') end
    if path==paint then return object('/Script/Engine.MaterialInterface') end
end
local M=require('appearance')
local parts={{mesh=mesh,materials={[0]={asset=paint}}}}
assert(M.resolve(parts)==parts and calls[1]==mesh and calls[2]==paint,'use complete Unreal object paths to load cold body and paint')
print('PASS: cold body and paint load with full object paths')
assert(parts[1].loadedMesh and parts[1].materials[0].loadedBase,'resolved assets survive preflight')
print('PASS: preflight resolves mesh and material before visual replacement')
local missing={{mesh=mesh,materials={}},{mesh='/Game/Vehicle/Missing.Missing',materials={}}}
local result,reason=M.resolve(missing)
assert(result==nil and reason:find('Missing'),'incomplete appearance must fail preflight')
print('PASS: missing parts fail the entire appearance preflight')
local before=#calls
assert(M.resolve({{mesh='/Game/Maps/Map.Map',materials={}}})==nil and #calls==before,'unrelated assets must not be loaded')
print('PASS: appearance loading remains restricted to vehicle assets')
local rimBase='/Game/Environment/Workbench/M_Workbench.M_Workbench'
local rimParts={{mesh=mesh,materials={[0]={asset=rimBase}}}}
local stockLoad=LoadAsset
function LoadAsset(path) if path==rimBase then return object('/Script/Engine.MaterialInterface') end;return stockLoad(path) end
assert(M.resolve(rimParts)==rimParts,'allow exact native rim color/metallic shader')
assert(M.resolve({{mesh=mesh,materials={[0]={asset='/Game/Environment/Workbench/Other.Other'}}}})==nil,'do not allow arbitrary environment materials')
print('PASS: exact rim shader can replicate without permitting unrelated environment assets')

local model={IsValid=function()return true end,GetAddress=function()return 1 end}
local scale=1
local function component(n,address)
    return {IsValid=function()return true end,GetFullName=function()return 'StaticMeshComponent /Game/Maps/Test.Test:Pawn.'..n end,
        GetAddress=function()return address end,GetAttachParent=function()return model end,bHiddenInGame=false,bVisible=true,
        StaticMesh={IsValid=function()return true end,GetFullName=function()return 'StaticMesh '..mesh end},
        K2_GetComponentScale=function()return {X=scale,Y=1,Z=1}end,K2_GetComponentToWorld=function()return {}end,GetNumMaterials=function()return 0 end}
end
local spring=component('Reflex_FL_spring',2)
local pawn={ModelRoot=model,GetActorScale3D=function()return {X=1,Y=1,Z=1}end,GetTransform=function()return {}end}
local mathLib={MakeRelativeTransform=function()return {}end,TransformLocation=function()return {X=0,Y=0,Z=0}end,
    TransformRotation=function()return {Pitch=0,Yaw=0,Roll=0}end}
local adapter={visualSources={spring},nextVisualScan=100,wheels={}}
local animation={key=function()return nil end}
local text,identity=M.capture(pawn,function()error('unexpected rescan')end,animation,adapter,mathLib,'run-1',1,nil,1)
scale=1.2
local nextText=M.capture(pawn,function()end,animation,adapter,mathLib,'run-1',2,identity,2)
assert(text and nextText==nil,'spring compression must not replace the car appearance')
print('PASS: live suspension scaling does not trigger a cosmetic rebuild')
adapter.visualSources={component('TireDisplay1_FL',3)}
local initial,initialIdentity=M.capture(pawn,function()end,animation,adapter,mathLib,'run-1',3,nil,3)
scale=1.3
assert(M.capture(pawn,function()end,animation,adapter,mathLib,'run-1',4,initialIdentity,4),'user tire scaling still changes appearance')
print('PASS: actual wheel size customization still refreshes the appearance')
''')

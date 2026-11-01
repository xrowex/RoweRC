"""Custom-shell preflight, restoration, color updates and travel lifecycle."""
import pathlib,sys
sys.path.insert(0,str(pathlib.Path('tools/python').resolve()))
from lupa.lua54 import LuaRuntime
lua=LuaRuntime(unpack_returned_tuples=True)
lua.execute("package.path='mod/RoweRC/Scripts/?.lua;'..package.path")
lua.execute('''
local saved,resets=nil,0
io.open=function(_,mode)if mode=='rb' then return nil end;return {write=function(self,t)saved=t;return self end,close=function()end} end
package.loaded.damage={reset=function()resets=resets+1 end}
function FName(n)return n end
local serial=0
local function object(path,class)
 serial=serial+1;local id=serial
 return {IsValid=function()return true end,IsA=function(_,c)return c==class end,
 GetAddress=function()return id end,GetFullName=function()return class..' '..path end}
end
local assets={}
function StaticFindObject()return nil end
function LoadAsset(path)
 if path:find('missing',1,true) then return nil end
 if not assets[path] then assets[path]=object(path,path:find('/SM_',1,true) and '/Script/Engine.StaticMesh' or '/Script/Engine.MaterialInterface') end
 return assets[path]
end
local original=object('/Game/Vehicle/Stock.Stock','/Script/Engine.StaticMesh')
local paint=object('/Game/Vehicle/Paint.Paint','/Script/Engine.MaterialInstanceDynamic');paint.color={R=.2,G=.3,B=.4,A=1}
paint.K2_GetVectorParameterValue=function(self)return self.color end
local mats={[0]=paint};local currentTransform={original=true}
local body=object('Body','StaticMeshComponent');body.StaticMesh=original
body.GetNumMaterials=function()return 1 end
body.GetMaterial=function(_,slot)return mats[slot] end
body.SetMaterial=function(_,slot,value)mats[slot]=value end
body.GetRelativeTransform=function()return currentTransform end
body.K2_SetRelativeTransform=function(_,t)currentTransform=t end
body.SetStaticMesh=function(self,mesh)assert(mesh:IsValid(),'never assign an unloaded cached mesh');self.StaticMesh=mesh;return true end
body.CreateDynamicMaterialInstance=function(_,slot,base)
 local mid=object('CustomPaint','/Script/Engine.MaterialInstanceDynamic')
 mid.SetVectorParameterValue=function(self,n,color)assert(n=='BodyColor');self.color=color end
 mats[slot]=mid;return mid
end
local pawn={BodyShell=body,GetAddress=function()return 100 end}
local M=require('shells')
assert(M.selected()=='stock')
M.select(pawn,'optional_shell')
assert(body.StaticMesh~=original and mats[0].color==paint.color and saved=='optional_shell\\n')
paint.color={R=.9,G=.1,B=.2,A=1};M.update(pawn);assert(mats[0].color==paint.color,'native color picker updates custom paint')
M.select(pawn,'supra_rc');assert(M.selected()=='supra_rc' and mats[5],'all Supra material groups apply')
M.select(pawn,'stock');assert(body.StaticMesh==original and mats[0]==paint and saved=='stock\\n','restore original mesh and materials')
local oldAssets=assets;assets={}
for _,asset in pairs(oldAssets)do asset.IsValid=function()return false end end
M.select(pawn,'supra_rc');assert(body.StaticMesh:IsValid(),'reacquire a shell after GC unloads previous assets')
M.select(pawn,'stock')
local before=body.StaticMesh
assert(not pcall(M.select,pawn,'../../escape') and body.StaticMesh==before,'reject unknown shells before mutation')
local loader=LoadAsset;LoadAsset=function()return nil end;M.clear()
assert(not pcall(M.select,pawn,'supra_rc') and body.StaticMesh==before and M.selected()=='stock','missing pack preserves stock car')
LoadAsset=loader;M.select(pawn,'optional_shell');M.clear();body.StaticMesh=original;mats[0]=paint
M.update(pawn);assert(body.StaticMesh~=original,'persist selection through map travel')
print('PASS: shell preflight, both selections, native paint, stock restore, invalid/missing packs and map travel')
''')

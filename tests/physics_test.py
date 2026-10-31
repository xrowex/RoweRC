import pathlib,sys
sys.path.insert(0,str(pathlib.Path('tools/python').resolve()))
from lupa.lua54 import LuaRuntime
lua=LuaRuntime(unpack_returned_tuples=True)
lua.execute("package.path='mod/RoweRC/Scripts/?.lua;'..package.path")
lua.execute(r'''
RegisterCustomProperty=function() end; PropertyTypes={Int64Property={}}
local M=require('physics'); local checks=0
local function check(ok,name) assert(ok,name); checks=checks+1; print('PASS: '..name) end
local circle={}; for i=0,99 do circle[#circle+1]={math.cos(i*math.pi/50),math.sin(i*math.pi/50)} end
local hull=M.hull(circle); local inside=true
for i,a in ipairs(hull) do local b=hull[i%#hull+1]
    for _,p in ipairs(circle) do if (b[1]-a[1])*(p[2]-a[2])-(b[2]-a[2])*(p[1]-a[1])< -1e-8 then inside=false end end
end
check(#hull<=32 and inside,'bounded support hull contains the source mesh silhouette')
local function mesh(name,extent)
    return {IsValid=function() return true end,GetFullName=function() return name end,bAllowCPUAccess=true,
        GetNumSections=function() return 1 end,GetBounds=function() return {Origin={X=0,Y=0,Z=0},BoxExtent=extent} end}
end
local bodyMesh=mesh('body',{X=20,Y=10,Z=5}); local wheelMesh=mesh('wheel',{X=3,Y=2,Z=3})
StaticFindObject=function() return {GetSectionFromStaticMesh=function(_,m,lod,section,vertices)
    for _,p in ipairs({{-20,-10},{20,-10},{20,10},{-20,10}}) do local v={X=p[1],Y=p[2],Z=0}; vertices[#vertices+1]={get=function() return v end} end
end} end
local function component(m,x,y)
    return {IsValid=function() return true end,StaticMesh=m,K2_GetComponentScale=function() return {X=1,Y=1,Z=1} end,
        K2_GetComponentToWorld=function() return {x=x,y=y} end}
end
local pawn={bDriving=true,GetAddress=function() return 1 end,GetTransform=function() return {} end,
    K2_GetActorLocation=function() return {X=100,Y=200,Z=0} end,K2_GetActorRotation=function() return {Yaw=0} end,
    BodyShell=component(bodyMesh,0,0)}
local function put(offset,v) pawn[string.format('RoweContact_%x',offset)]=string.unpack('i8',string.pack('d',v)) end
local function get(offset) return string.unpack('d',string.pack('i8',pawn[string.format('RoweContact_%x',offset)])) end
for offset=0x7748,0x7778,8 do put(offset,0) end
put(0x7748,1); put(0x7750,2); put(0x7758,1)
local adapter={wheels={component(wheelMesh,12,-8),component(wheelMesh,12,8),component(wheelMesh,-12,-8),component(wheelMesh,-12,8)}}
local mathLib={MakeRelativeTransform=function(_,source) return source end,
    TransformLocation=function(_,relative,v) return {X=v.X+relative.x,Y=v.Y+relative.y,Z=v.Z} end}
local events=''; local function read() return events end; local function write() end
local function capture(now) return M.capture(pawn,adapter,mathLib,now,'room-1','run-1',read,write) end
local text=capture(10)
check(text:find('-0.2000:-0.1000',1,true)~=nil and text:find('0.2000:0.1000',1,true)~=nil,'mesh centimetres convert to correctly sized metre colliders')
local count=0; for _ in text:gmatch(';') do count=count+1 end
check(count==4,'body hull and all four tire cylinders are included')
events='room-1\n7\trun-1\t1\t10\t100\t200\t0.5\t0\t0.1\n'
capture(10.1); capture(10.2)
check(math.abs(get(0x7768)-.5)<1e-8 and math.abs(get(0x7778)-.1)<1e-8,'owner applies contact velocity and yaw exactly once')
events='old-room\n8\trun-1\t1\t10\t100\t200\t1\t0\t1\n'; capture(10.3)
check(math.abs(get(0x7768)-.5)<1e-8,'old lobby contact events cannot affect the car')
put(0x7748,0); put(0x7768,0); put(0x7778,0)
events='room-1\n9\trun-1\t1\t10\t100\t200\t1\t0\t1\n'; capture(10.4)
check(get(0x7768)==0 and get(0x7778)==0,'reset generation rejects in-flight contact events')
events='room-1\n10\trun-1\t2\t8\t100\t200\t1\t0\t1\n'; capture(10.5)
check(get(0x7768)==0,'expired contact events are acknowledged without applying a late bump')
put(0x7750,99)
check(not pcall(capture,10.6) and get(0x7768)==0,'native pose mismatch prevents all writes')
print(checks..' physics adapter checks passed')
''')

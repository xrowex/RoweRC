-- Native planar dynamics adapter, pinned to executable 08da7cc9...92b4cf.
-- The companion enables this only after verifying the supported game files.
local M={}
local registered,registrationError=pcall(function()
    for _,offset in ipairs({0x7568,0x7580,0x7750,0x7758,0x7760,0x7768,0x7770,0x7778,0x7780}) do
        RegisterCustomProperty({Name=string.format("RoweContact_%x",offset),Type=PropertyTypes.Int64Property,
            BelongsToClass="/Script/RCDriftSim.RCDriftPawn",OffsetInternal=offset})
    end
end)
local function get(pawn,offset) return string.unpack("d",string.pack("i8",pawn[string.format("RoweContact_%x",offset)])) end
local function put(pawn,offset,value) pawn[string.format("RoweContact_%x",offset)]=string.unpack("i8",string.pack("d",value)) end
local function valid(o) return o and o:IsValid() end
local function finite(n) return type(n)=="number" and n==n and math.abs(n)<1e8 end
local generation,previousSim,previousPawn,lastAck,key=0,nil,nil,0,""
local hullCache={}
-- Offline mesh outlines in centimetres; avoids large reflected vertex arrays in play.
local customHulls={
    ['/Game/Vehicle/RoweRC/Shells/optional_shell/SM_optional_shell.SM_optional_shell']={{-22.75,-8.55},{-22.63,-9.11},{-22.53,-9.36},{-22.36,-9.6},{-22.07,-9.74},{-20.92,-9.99},{-16.03,-10.71},{-15.77,-10.73},{7.02,-12.24},{7.28,-12.25},{18.34,-10.74},{20.8,-10.21},{22.01,-9.53},{22.2,-9.35},{22.32,-9.18},{22.47,-8.8},{22.71,-6.71},{22.89,-4.42},{22.95,-3.1},{23.0,-1.39},{23.02,0.76},{23.0,2.9},{22.95,4.61},{22.89,5.93},{22.71,8.22},{22.47,10.31},{22.32,10.69},{22.2,10.86},{22.01,11.04},{20.8,11.72},{18.34,12.25},{-14.9,12.25},{-15.77,12.22},{-16.03,12.21},{-20.92,11.49},{-22.07,11.24},{-22.36,11.1},{-22.53,10.86},{-22.63,10.61},{-22.75,10.05}},
    ['/Game/Vehicle/RoweRC/Shells/supra_rc/SM_supra_rc.SM_supra_rc']={{-22.75,-0.49},{-22.65,-5.28},{-22.64,-5.3},{-21.83,-6.86},{-21.68,-7.1},{-21.5,-7.33},{-21.29,-7.56},{-21.06,-7.76},{-20.82,-7.92},{-20.57,-8.06},{-15.69,-10.51},{-15.67,-10.52},{-15.64,-10.53},{-15.56,-10.55},{-15.41,-10.56},{-15.18,-10.57},{1.96,-11.3},{2.17,-11.3},{16.6,-10.67},{16.65,-10.66},{16.82,-10.62},{17.15,-10.53},{17.66,-10.39},{18.14,-10.25},{18.67,-10.07},{19.12,-9.9},{19.56,-9.7},{19.92,-9.52},{20.23,-9.34},{20.49,-9.17},{20.69,-9.01},{20.86,-8.86},{21.01,-8.7},{21.15,-8.54},{21.28,-8.37},{21.42,-8.17},{21.56,-7.95},{21.71,-7.68},{21.85,-7.41},{22.01,-7.09},{22.16,-6.76},{22.29,-6.44},{22.42,-6.1},{22.53,-5.77},{22.64,-5.39},{22.74,-4.99},{22.83,-4.54},{22.9,-4.02},{22.96,-3.39},{23.0,-2.65},{23.01,-1.82},{23.02,-0.93},{23.02,0.93},{23.01,1.82},{23.0,2.65},{22.96,3.39},{22.9,4.02},{22.83,4.54},{22.74,4.99},{22.64,5.39},{22.53,5.77},{22.42,6.1},{22.29,6.44},{22.16,6.76},{22.01,7.09},{21.85,7.41},{21.71,7.68},{21.56,7.95},{21.42,8.17},{21.28,8.37},{21.15,8.54},{21.01,8.7},{20.86,8.86},{20.69,9.01},{20.49,9.17},{20.23,9.34},{19.92,9.52},{19.56,9.7},{19.12,9.9},{18.67,10.07},{18.14,10.25},{17.66,10.39},{17.15,10.53},{16.82,10.62},{16.65,10.66},{16.6,10.67},{2.17,11.3},{1.96,11.3},{-15.18,10.57},{-15.41,10.56},{-15.56,10.55},{-15.64,10.53},{-15.67,10.52},{-15.69,10.51},{-20.57,8.06},{-20.82,7.92},{-21.06,7.76},{-21.29,7.56},{-21.5,7.33},{-21.68,7.1},{-21.83,6.86},{-21.94,6.64},{-22.03,6.41},{-22.04,6.38},{-22.11,6.14},{-22.17,5.89},{-22.23,5.58},{-22.29,5.25},{-22.35,4.9},{-22.41,4.53},{-22.45,4.25},{-22.49,3.94},{-22.59,3.0},{-22.66,2.28},{-22.69,1.87},{-22.71,1.44},{-22.73,0.97},{-22.75,0.49}},
}
local base=(os.getenv("LOCALAPPDATA") or ".").."/RoweRC/bridge/"
local function cross(a,b,c) return (b[1]-a[1])*(c[2]-a[2])-(b[2]-a[2])*(c[1]-a[1]) end
function M.hull(points)
    table.sort(points,function(a,b) return a[1]==b[1] and a[2]<b[2] or a[1]<b[1] end)
    local unique={}
    for _,p in ipairs(points) do local q=unique[#unique]; if not q or p[1]~=q[1] or p[2]~=q[2] then unique[#unique+1]=p end end
    assert(#unique>=3,"Mesh has no planar outline")
    local lower,upper={},{}
    for _,p in ipairs(unique) do while #lower>=2 and cross(lower[#lower-1],lower[#lower],p)<=1e-10 do table.remove(lower) end; lower[#lower+1]=p end
    for i=#unique,1,-1 do local p=unique[i]; while #upper>=2 and cross(upper[#upper-1],upper[#upper],p)<=1e-10 do table.remove(upper) end; upper[#upper+1]=p end
    table.remove(lower); table.remove(upper); for _,p in ipairs(upper) do lower[#lower+1]=p end
    if #lower<=48 then return lower end
    -- Circumscribed support polygon retains the mesh footprint. At RC scale,
    -- 48 support planes follow the silhouette closely without networked vertices.
    local planes={}
    for i=0,47 do local a=i*math.pi/24; local x,y=math.cos(a),math.sin(a); local d=-math.huge
        for _,p in ipairs(lower) do d=math.max(d,x*p[1]+y*p[2]) end
        planes[#planes+1]={x,y,d}
    end
    local result={}
    for i,a in ipairs(planes) do local b=planes[i%48+1]; local det=a[1]*b[2]-a[2]*b[1]
        result[#result+1]={(a[3]*b[2]-a[2]*b[3])/det,(a[1]*b[3]-a[3]*b[1])/det}
    end
    return M.hull(result) -- Remove duplicate/collinear intersections at sharp mesh corners.
end
local function bodyHull(mesh)
    local path=mesh:GetFullName()
    if hullCache[path] then return hullCache[path] end
    local baked=customHulls[path:match('^[^ ]+ (.+)$')]
    if baked then local hull=M.hull(baked);hullCache[path]=hull;return hull end
    assert(mesh.bAllowCPUAccess,"Body mesh has no CPU vertices; contact remains disabled for this car")
    local library=StaticFindObject("/Script/ProceduralMeshComponent.Default__KismetProceduralMeshLibrary")
    local points,seen={},{}
    local sections=mesh:GetNumSections(0); assert(sections>0 and sections<=16,"Unsupported body sections")
    for section=0,sections-1 do
        local vertices,triangles,normals,uv,tangents={},{},{},{},{}
        library:GetSectionFromStaticMesh(mesh,0,section,vertices,triangles,normals,uv,tangents)
        assert(#vertices<=1000000,"Body mesh exceeds collider extraction budget")
        for _,entry in ipairs(vertices) do
            local v=entry:get(); local x,y=math.floor(v.X*100+.5)/100,math.floor(v.Y*100+.5)/100
            local identity=x..":"..y
            if not seen[identity] then seen[identity]=true; points[#points+1]={x,y} end
        end
    end
    local hull=M.hull(points); hullCache[path]=hull; return hull
end
local function outline(points,relative,mathLib)
    local result={}
    for _,p in ipairs(points) do
        local v=mathLib:TransformLocation(relative,{X=p[1],Y=p[2],Z=p[3] or 0})
        result[#result+1]={math.floor(v.X*100+0.5)/10000,math.floor(v.Y*100+0.5)/10000}
    end
    -- Input is centimetres; .01 cm quantization becomes .0001 m.
    return M.hull(result)
end
local function shape(pawn,adapter,mathLib)
    local body=pawn.BodyShell; assert(valid(body) and valid(body.StaticMesh),"No body mesh")
    local parent=pawn:GetTransform()
    local relative=mathLib:MakeRelativeTransform(body:K2_GetComponentToWorld(),parent)
    local polygons={outline(bodyHull(body.StaticMesh),relative,mathLib)}
    for i=1,4 do
        local wheel=adapter.wheels[i]; assert(valid(wheel) and valid(wheel.StaticMesh),"No tire mesh")
        local bounds=wheel.StaticMesh:GetBounds(); local e,c=bounds.BoxExtent,bounds.Origin
        local extents={e.X,e.Y,e.Z}; local center={c.X,c.Y,c.Z}; local axis=1
        for j=2,3 do if extents[j]<extents[axis] then axis=j end end
        local first=axis%3+1; local second=first%3+1; local points={}
        local radius=math.max(extents[first],extents[second])
        for side=-1,1,2 do for segment=0,15 do
            local angle=segment*math.pi/8; local p={center[1],center[2],center[3]}
            p[axis]=p[axis]+side*extents[axis]; p[first]=p[first]+radius*math.cos(angle); p[second]=p[second]+radius*math.sin(angle)
            points[#points+1]=p
        end end
        polygons[#polygons+1]=outline(points,mathLib:MakeRelativeTransform(wheel:K2_GetComponentToWorld(),parent),mathLib)
    end
    local encoded={}; local vertices=0
    for _,polygon in ipairs(polygons) do local text={}; vertices=vertices+#polygon
        for _,p in ipairs(polygon) do text[#text+1]=string.format("%.4f:%.4f",p[1],p[2]) end
        encoded[#encoded+1]=table.concat(text,"|")
    end
    assert(vertices<=128,"Collider exceeds point limit")
    local text=table.concat(encoded,";"); assert(#text<=2300,"Collider exceeds packet budget")
    local bounds=body.StaticMesh:GetBounds().BoxExtent
    local scale=body:K2_GetComponentScale()
    local inertia=1.5*((bounds.X*scale.X*.02)^2+(bounds.Y*scale.Y*.02)^2)/12
    return text,math.max(.0001,math.min(2,inertia))
end
function M.capture(pawn,adapter,mathLib,now,contactKey,run,read,write)
    if not registered then error(registrationError) end
    if key~=contactKey then key=contactKey; lastAck=0 end
    local sim=get(pawn,0x7750)
    if previousPawn~=pawn:GetAddress() or previousSim and sim<previousSim-.001 then generation=generation+1 end
    previousPawn=pawn:GetAddress(); previousSim=sim
    local p,r=pawn:K2_GetActorLocation(),pawn:K2_GetActorRotation()
    local nativeX,nativeY,yaw=get(pawn,0x7760)*100,get(pawn,0x7758)*100,get(pawn,0x7768)
    assert(finite(nativeX) and finite(nativeY) and finite(yaw) and math.abs(nativeX-p.X)<.2 and math.abs(nativeY-p.Y)<.2
        and math.abs((math.deg(yaw)-r.Yaw+180)%360-180)<.2,"Native dynamics layout mismatch")
    local events=read("contact-events.tsv",16384) or ""
    if events:match("^([^\r\n]+)")==key then
        for line in events:gmatch("[^\r\n]+") do
            local event={}; for v in (line.."\t"):gmatch("(.-)\t") do event[#event+1]=v end
            if #event==9 or #event==11 then
                local id=tonumber(event[1])
                if id and id>lastAck then
                    lastAck=id
                    local gen,time,x,y,dx,dy,dw=tonumber(event[3]),tonumber(event[4]),tonumber(event[5]),tonumber(event[6]),tonumber(event[7]),tonumber(event[8]),tonumber(event[9])
                    local px,py=tonumber(event[10] or "0"),tonumber(event[11] or "0")
                    local speed=math.sqrt(get(pawn,0x7770)^2+get(pawn,0x7778)^2)
                    if event[2]==run and gen==generation and finite(time) and now-time>=0 and now-time<.65 and pawn.bDriving
                        and finite(x) and finite(y) and (p.X-x)^2+(p.Y-y)^2<(80+speed*100*(now-time))^2
                        and finite(px) and finite(py) and px*px+py*py<=.0004
                        and finite(dx) and finite(dy) and finite(dw) and dx*dx+dy*dy<=16.001 and math.abs(dw)<=10 then
                        local c,s=math.cos(yaw),math.sin(yaw)
                        local u,v,w=get(pawn,0x7770),get(pawn,0x7778),get(pawn,0x7780)
                        local nextU,nextV,nextW=u+c*dx+s*dy,v-s*dx+c*dy,w+dw
                        if math.abs(nextU)<30 and math.abs(nextV)<30 and math.abs(nextW)<30 then
                            put(pawn,0x7760,get(pawn,0x7760)+px); put(pawn,0x7758,get(pawn,0x7758)+py)
                            put(pawn,0x7770,nextU); put(pawn,0x7778,nextV); put(pawn,0x7780,nextW)
                            write("contact-last.txt",string.format("Applied contact %d / dv %.3f %.3f / yaw %.3f",id,dx,dy,dw))
                        end
                    end
                end
            end
        end
    end
    local hull=shape(pawn,adapter,mathLib)
    local mass,inertia=get(pawn,0x7568),get(pawn,0x7580)
    assert(finite(mass) and mass>=.1 and mass<=10 and finite(inertia) and inertia>=.0001 and inertia<=2,"Invalid native mass/inertia")
    local u,v,w=get(pawn,0x7770),get(pawn,0x7778),get(pawn,0x7780)
    local c,s=math.cos(yaw),math.sin(yaw)
    return string.format("%.9g,%d,%.8g,%.8g,%.8g,%.8g,%.8g,%d,%d,%s",now,generation,c*u-s*v,s*u+c*v,w,mass,inertia,lastAck,pawn.bDriving and 1 or 0,hull)
end
return M

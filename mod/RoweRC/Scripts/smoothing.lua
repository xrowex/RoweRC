-- Pure snapshot interpolation; no Unreal object access. Sender clocks need not match.
local M = { delay = .12, extrapolate = .10, teleport = 400 }
local function clamp(x, a, b) return math.max(a, math.min(b, x)) end
local function quat(p, y, r)
    p,y,r=math.rad(p)/2,math.rad(y)/2,math.rad(r)/2
    local sp,cp,sy,cy,sr,cr=math.sin(p),math.cos(p),math.sin(y),math.cos(y),math.sin(r),math.cos(r)
    return {cr*sp*sy-sr*cp*cy,-cr*sp*cy-sr*cp*sy,cr*cp*sy-sr*sp*cy,cr*cp*cy+sr*sp*sy}
end
local function rotation(a,b,t,first,out)
    local q,v=quat(a[first],a[first+1],a[first+2]),quat(b[first],b[first+1],b[first+2])
    local dot=0; for i=1,4 do dot=dot+q[i]*v[i] end
    if dot<0 then for i=1,4 do v[i]=-v[i] end; dot=-dot end
    local x,y=1-t,t
    if dot<.9995 then local theta=math.acos(clamp(dot,-1,1)); local sine=math.sin(theta); x=math.sin((1-t)*theta)/sine; y=math.sin(t*theta)/sine end
    local s,n={},0; for i=1,4 do s[i]=q[i]*x+v[i]*y; n=n+s[i]^2 end
    n=math.sqrt(n); for i=1,4 do s[i]=s[i]/n end
    local qx,qy,qz,qw=s[1],s[2],s[3],s[4]
    out[first]=-math.deg(math.asin(clamp(2*(qw*qy-qz*qx),-1,1)))
    out[first+1]=math.deg(math.atan(2*(qw*qz+qx*qy),1-2*(qy*qy+qz*qz)))
    out[first+2]=-math.deg(math.atan(2*(qw*qx+qy*qz),1-2*(qx*qx+qy*qy)))
end
function M.blend(a,b,t)
    local result={}
    for i=1,3 do result[i]=a[i]+(b[i]-a[i])*t end
    rotation(a,b,t,4,result)
    return result
end
function M.push(buffer, frame, now)
    local previous=buffer[#buffer]
    if previous and previous.run==frame.run and frame.seq<=previous.seq then return false end
    local jump=previous and ((frame.pose[1]-previous.pose[1])^2+(frame.pose[2]-previous.pose[2])^2+(frame.pose[3]-previous.pose[3])^2>M.teleport^2)
    local timestamp=frame.motion and frame.motion[1] or now
    if previous and (previous.run~=frame.run or jump or timestamp<=previous.time or timestamp-previous.time>2) then
        for i=#buffer,1,-1 do buffer[i]=nil end; buffer.offset=nil; previous=nil
    end
    frame.time=timestamp; frame.arrived=now
    local offset=now-timestamp
    -- Prefer the lowest observed transit delay; relax slowly for clock drift.
    buffer.offset=buffer.offset and math.min(offset,buffer.offset+math.max(0,now-(buffer.lastArrival or now))*.002) or offset
    buffer.lastArrival=now; buffer[#buffer+1]=frame
    if #buffer>32 then table.remove(buffer,1) end
    return true
end
function M.sample(buffer,now)
    if #buffer==0 then return nil end
    local last=buffer[#buffer]
    if now-last.arrived>2 then return nil end
    local target=now-(buffer.offset or 0)-M.delay
    local a,b=buffer[1],buffer[1]
    for i=2,#buffer do b=buffer[i]; if b.time>=target then break end; a=b end
    if target>=last.time then
        a=buffer[math.max(1,#buffer-1)]; b=last
    end
    local t=b.time>a.time and clamp((target-a.time)/(b.time-a.time),0,1) or 1
    local pose=M.blend(a.pose,b.pose,t)
    if target>last.time and b.time>a.time then
        local ahead=clamp(target-last.time,0,M.extrapolate)
        for i=1,3 do pose[i]=b.pose[i]+clamp((b.pose[i]-a.pose[i])/(b.time-a.time),-20000,20000)*ahead end
    end
    local motion
    if a.motion and b.motion then
        motion={target}
        for wheel=0,7 do
            local start=2+wheel*6; local pa,pb={},{}
            for j=1,6 do pa[j]=a.motion[start+j-1]; pb[j]=b.motion[start+j-1] end
            local blended=M.blend(pa,pb,t)
            for j=1,6 do motion[start+j-1]=blended[j] end
        end
        for i=50,67 do motion[i]=i%3==1 and (t<.5 and a.motion[i] or b.motion[i]) or a.motion[i]+(b.motion[i]-a.motion[i])*t end
    else motion=b.motion end
    return {pose=pose,motion=motion,age=now-last.arrived}
end
return M

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
local function reset(buffer)
    for key in pairs(buffer) do buffer[key]=nil end
end
function M.push(buffer, frame, now)
    local previous=buffer[#buffer]
    if previous and previous.run==frame.run and frame.seq<=previous.seq then return false end
    local jump=previous and ((frame.pose[1]-previous.pose[1])^2+(frame.pose[2]-previous.pose[2])^2+(frame.pose[3]-previous.pose[3])^2>M.teleport^2)
    local timestamp=frame.motion and frame.motion[1] or now
    if previous and (previous.run~=frame.run or jump or timestamp<=previous.time or timestamp-previous.time>2) then reset(buffer); previous=nil end
    frame.time=timestamp; frame.arrived=now
    local offset=now-timestamp
    if previous then
        local interval=timestamp-previous.time
        local variation=math.abs((now-previous.arrived)-interval)
        buffer.interval=(buffer.interval or interval)*.9+interval*.1
        buffer.jitter=(buffer.jitter or 0)*.9+math.min(.25,variation)*.1
    end
    buffer.offset=buffer.offset and math.min(offset,buffer.offset+math.max(0,now-(buffer.lastArrival or now))*.002) or offset
    buffer.lastArrival=now; buffer[#buffer+1]=frame
    buffer.delay=#buffer>=12 and clamp(2*(buffer.interval or .05)+2*(buffer.jitter or 0),M.delay,.24) or M.delay
    if #buffer>48 then table.remove(buffer,1) end
    return true
end
local function position(buffer,ai,bi,t,pose)
    local a,b=buffer[ai],buffer[bi]
    if ai==bi then return end
    local span=b.time-a.time
    local before,after=buffer[ai-1],buffer[bi+1]
    for axis=1,3 do
        local slope=(b.pose[axis]-a.pose[axis])/span
        local va=before and (b.pose[axis]-before.pose[axis])/(b.time-before.time) or slope
        local vb=after and (after.pose[axis]-a.pose[axis])/(after.time-a.time) or slope
        if math.abs(slope)<1e-8 then va=0; vb=0 else
            if va*slope<=0 then va=0 end; if vb*slope<=0 then vb=0 end
            local magnitude=(va/slope)^2+(vb/slope)^2
            if magnitude>9 then local scale=3/math.sqrt(magnitude); va=va*scale; vb=vb*scale end
        end
        pose[axis]=(2*t^3-3*t^2+1)*a.pose[axis]+(t^3-2*t^2+t)*span*va
            +(-2*t^3+3*t^2)*b.pose[axis]+(t^3-t^2)*span*vb
    end
end
function M.sample(buffer,now)
    if #buffer==0 then return nil end
    local last=buffer[#buffer]
    if now-last.arrived>2 then return nil end
    local desired=now-(buffer.offset or 0)-(buffer.delay or M.delay)
    local dt=buffer.sampledAt and clamp(now-buffer.sampledAt,0,.25) or 0
    if not buffer.playhead then buffer.playhead=desired else
        local error=desired-(buffer.playhead+dt)
        -- A monotonic playout clock: earlier arrivals and buffer changes adjust speed,
        -- never jump the timeline backwards or directly chase a packet timestamp.
        local speed=clamp(1+error*2,.85,1.15)
        buffer.playhead=math.min(last.time+M.extrapolate,buffer.playhead+dt*speed)
    end
    buffer.sampledAt=math.max(now,buffer.sampledAt or now)
    local target=buffer.playhead
    local ai,bi=1,1
    for i=2,#buffer do bi=i; if buffer[i].time>=target then break end; ai=i end
    if target>=last.time then ai=math.max(1,#buffer-1); bi=#buffer end
    local a,b=buffer[ai],buffer[bi]
    local t=b.time>a.time and clamp((target-a.time)/(b.time-a.time),0,1) or 1
    local pose=M.blend(a.pose,b.pose,t)
    position(buffer,ai,bi,t,pose)
    local predicting=target>last.time
    if predicting and b.time>a.time then
        local ahead=clamp(target-last.time,0,M.extrapolate)
        for i=1,3 do pose[i]=b.pose[i]+clamp((b.pose[i]-a.pose[i])/(b.time-a.time),-20000,20000)*ahead end
    end
    if buffer.predicting and not predicting and buffer.output and dt>0 then
        local correction={}; local distance=0
        for i=1,3 do correction[i]=buffer.output[i]+(buffer.velocity and buffer.velocity[i] or 0)*dt-pose[i]; distance=distance+correction[i]^2 end
        if distance<10000 then buffer.correction=correction end
    end
    if buffer.correction then
        local decay=math.exp(-dt/.08)
        for i=1,3 do buffer.correction[i]=buffer.correction[i]*decay; pose[i]=pose[i]+buffer.correction[i] end
    end
    if buffer.output and dt>0 then
        buffer.velocity={}; for i=1,3 do buffer.velocity[i]=clamp((pose[i]-buffer.output[i])/dt,-20000,20000) end
    end
    buffer.output=pose; buffer.predicting=predicting
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
    return {pose=pose,motion=motion,age=now-last.arrived,delay=buffer.delay or M.delay,predicting=predicting}
end
return M

import pathlib,sys
sys.path.insert(0,str(pathlib.Path('tools/python').resolve()))
from lupa.lua54 import LuaRuntime
lua=LuaRuntime(unpack_returned_tuples=True)
lua.execute("package.path='mod/RoweRC/Scripts/?.lua;'..package.path")
lua.execute('''
local M=require('smoothing')
local checks=0
local function check(ok,name) assert(ok,name); checks=checks+1; print('PASS: '..name) end
local function close(a,b) return math.abs(a-b)<.001 end
local function frame(seq,t,x,yaw,motion)
    return {run='a',seq=seq,pose={x,0,0,0,yaw or 0,0},motion=motion and motion or nil}
end
local function moving(seq,t,x)
    local m={t}; for i=2,49 do m[i]=0 end
    for i=50,67 do m[i]=(i%3==0 and 1 or 0) end
    return frame(seq,t,x,0,m)
end
local b={}
M.push(b,moving(1,100,0),10); M.push(b,moving(2,100.1,10),10.1); M.push(b,moving(3,100.2,20),10.2)
check(close(M.sample(b,10.2).pose[1],8),'render 120ms behind snapshots using sender clock offset')
check(not M.push(b,moving(2,100.1,999),10.21) and #b==3,'duplicate / reordered snapshots cannot rewind')
local first=M.sample(b,10.24).pose[1]
for i=1,20 do M.sample(b,10.2+i*.002) end
check(close(first,M.sample(b,10.24).pose[1]),'result is independent of render frame count')
check(close(M.sample(b,11).pose[1],30),'late packets extrapolate for at most 100ms then freeze')
check(M.sample(b,12.3)==nil,'stale snapshot expires')
M.push(b,moving(4,100.3,900),10.3)
check(#b==1 and close(M.sample(b,10.3).pose[1],900),'teleports reset interpolation instead of sliding')
local restart=moving(1,1,5); restart.run='b'; M.push(b,restart,10.4)
check(#b==1 and close(M.sample(b,10.4).pose[1],5),'new game run resets sender timeline')
local r=M.blend({0,0,0,0,170,0},{0,0,0,0,-170,0},.5)
check(close(math.abs(r[5]),180),'quaternion interpolation takes the short path through 180 degrees')
local original={1,2,3,20,40,-70}; r=M.blend(original,original,.7)
check(close(r[4],20) and close(r[5],40) and close(r[6],-70),'Unreal rotator / quaternion conversion preserves mixed-axis rotation')
local w={}; local a=moving(1,10,0); local c=moving(2,10.1,10)
a.motion[5]=10; c.motion[5]=30; a.motion[50]=0; c.motion[50]=1
M.push(w,a,20); M.push(w,c,20.1)
local sample=M.sample(w,20.17)
check(close(sample.motion[5],20) and close(sample.motion[50],.5),'wheels and motor audio share body interpolation timeline')
local jitter={}
for i=0,19 do M.push(jitter,moving(i,10+i*.05,i*5),20+i*.05+(i%3)*.01) end
check(math.abs(M.sample(jitter,20.95).pose[1]-83)<2,'buffer absorbs varying packet arrival times')

local clock={}
for i=0,15 do M.push(clock,moving(i,10+i*.05,i*5),20+i*.05+.04) end
M.sample(clock,20.8)
local oldHead=clock.playhead
M.push(clock,moving(16,10.8,80),20.8) -- A much earlier transit sample changes the minimum offset.
M.sample(clock,20.81)
check(clock.playhead>=oldHead and clock.playhead-oldHead<=.01151,'clock-offset improvements slew playback instead of jumping it')
local slow={}
for i=0,19 do M.push(slow,moving(i,10+i*.1,i*10),20+i*.1) end
check(slow.delay>=.19 and slow.delay<=.24,'slow 10Hz peers receive enough interpolation headroom')
local fast={}
for i=0,29 do M.push(fast,moving(i,10+i/30,i*3),20+i/30) end
check(close(fast.delay,.12),'30Hz peers keep the low base interpolation delay')
local curve={}
for i=0,9 do M.push(curve,moving(i,10+i*.1,100*(i*.1)^2),20+i*.1) end
local function curveAt(target)
    curve.playhead=target; curve.sampledAt=target+10+curve.delay; curve.output=nil; curve.correction=nil; curve.predicting=false
    return M.sample(curve,curve.sampledAt).pose[1]
end
local h=1/240
local before,center,after=curveAt(10.4-h),curveAt(10.4),curveAt(10.4+h)
local velocityChange=math.abs((after-center)/h-(center-before)/h)
check(velocityChange<2,'cubic body interpolation removes velocity steps at snapshot boundaries')
local recovery={}
M.push(recovery,moving(1,10,0),20); M.push(recovery,moving(2,10.1,10),20.1); M.push(recovery,moving(3,10.2,20),20.2)
local predicted=M.sample(recovery,20.41).pose[1]
M.push(recovery,moving(4,10.4,25),20.42)
local corrected=M.sample(recovery,20.42).pose[1]
check(math.abs(predicted-corrected)<2,'recovery from prediction blends its small position error')
local monotonic={}; local previous=-math.huge; local monotonicGood=true
for i=0,40 do
    local now=30+i/60
    if i%3==0 then M.push(monotonic,moving(i,10+i/60,i*2),now+(i%2)*.004) end
    M.sample(monotonic,now+.004)
    monotonicGood=monotonicGood and monotonic.playhead>=previous
    previous=monotonic.playhead
end
check(monotonicGood,'playback remains monotonic through jittered render frames')
local turns={}
for i=0,9 do local f=moving(i,10+i*.1,i); f.pose[5]=(175+100*(i*.1)^2+180)%360-180; M.push(turns,f,20+i*.1) end
local function headingAt(target)
    turns.playhead=target; turns.sampledAt=target+10+turns.delay; turns.output=nil; turns.correction=nil; turns.predicting=false
    return M.sample(turns,turns.sampledAt).pose[5]
end
local ya,yb,yc=headingAt(10.4-h),headingAt(10.4),headingAt(10.4+h)
check(math.abs((yc-yb)/h-(yb-ya)/h)<2,'heading acceleration stays continuous across snapshot boundaries')
check(math.abs(headingAt(10.23)-(-179.71))<.1,'cubic heading crosses the wrapped 180 degree boundary by the short path')
print(tostring(checks)..' smoothing checks passed')
''')

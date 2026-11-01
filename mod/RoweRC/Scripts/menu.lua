local UI = { open = false }
local UE = require("UEHelpers")
local Rims = require("rims")
local Shells = require("shells")
local base = (os.getenv("LOCALAPPDATA") or ".") .. "/RoweRC/bridge/"
local root, tree, statusLabel, mapLabel, codeLabel, joinInput
local buttons, actions = {}, {}
local selected, maps, requestId, lastClick = 1, {}, os.time() * 1000, 0
local oldCursor, wasDriving = false, false
local viewportScale = 1
local role = "idle"
local hostControls = {}
local mapButton, hostButton, publicButton, joinButton, updateLabel, browserLabel
local browserRows, lobbies, browserPage = {}, {}, 1
local noticeRoot, noticeVersion, previewUntil
local peerLabel,contactLabel,contactButton
local contactEnabled=false
local rimSlider,rimLabel,rimLast
local panels,tabButtons,currentTab={},{},'online'
local shellLabel,onlineMapLabel,leaveButton,copyButton
local browserPager
local function chooseTab(id)
    currentTab=id
    for name,panel in pairs(panels) do panel:SetVisibility(name==id and 0 or 1) end
    for name,tab in pairs(tabButtons) do tab:SetBackgroundColor(name==id and {R=.12,G=.42,B=.45,A=1} or {R=.06,G=.15,B=.19,A=1}) end
end
local function valid(o) return o and o:IsValid() end
local function read(name)
    local f = io.open(base .. name, "rb"); if not f then return "" end
    local s = f:read(65536) or ""; f:close(); return s
end
local function request(action, argument)
    requestId = requestId + 1
    local f = io.open(base .. "ui-request.tsv", "wb")
    if f then f:write(tostring(requestId), "\t", action, "\t", tostring(argument or ""), "\t", tostring(requestId), "\n"); f:close() end
end
local function make(class) return StaticConstructObject(StaticFindObject("/Script/UMG." .. class), tree) end
local function label(text, size)
    local w = make("TextBlock"); w:SetText(FText(text))
    w:SetColorAndOpacity({SpecifiedColor={R=.90,G=.95,B=.97,A=1},ColorUseRule=0})
    local font = w.Font; font.Size = math.floor((size or 15) / viewportScale); w:SetFont(font)
    w:SetAutoWrapText(true)
    return w
end
local function add(parent, child)
    local slot = parent:AddChildToVerticalBox(child)
    slot:SetPadding({Left=0,Top=4,Right=0,Bottom=4})
    return child
end
local function click(callback)
    local now = os.clock()
    if now - lastClick < .15 then return end
    lastClick = now; callback()
end
local function button(parent, text, callback)
    local w = StaticConstructObject(StaticFindObject("/Script/RCDriftSim.RCUIAction"), tree)
    local caption = label(text, 16); caption:SetAutoWrapText(false)
    w:SetContent(caption)
    w:SetBackgroundColor({R=.08,G=.24,B=.27,A=1})
    add(parent, w)
    buttons[#buttons + 1] = w
    actions[tostring(w:GetAddress())] = callback
    return w
end
function UI.Invalidate()
    if valid(noticeRoot) then noticeRoot:RemoveFromParent() end
    noticeRoot=nil; noticeVersion=nil
    UI.open = false; root = nil; tree = nil; buttons = {}; actions = {}; hostControls = {}; browserRows = {}
    rimSlider=nil; rimLabel=nil; rimLast=nil
    panels={};tabButtons={};shellLabel=nil;onlineMapLabel=nil
end
local function updateNotice(version,preview)
    if not version or not version:match("^%d+%.%d+%.%d+$") then
        if valid(noticeRoot) then noticeRoot:RemoveFromParent() end
        noticeRoot=nil; noticeVersion=nil; return
    end
    if valid(noticeRoot) and noticeVersion==version then return end
    local pc=UE.GetPlayerController()
    if not valid(pc) then return end
    if valid(noticeRoot) then noticeRoot:RemoveFromParent() end
    local lib=StaticFindObject("/Script/UMG.Default__WidgetBlueprintLibrary")
    local layout=StaticFindObject("/Script/UMG.Default__WidgetLayoutLibrary")
    noticeRoot=lib:Create(pc,StaticFindObject("/Script/UMG.UserWidget"),pc)
    if not valid(noticeRoot) then return end
    local nt=StaticConstructObject(StaticFindObject("/Script/UMG.WidgetTree"),noticeRoot)
    noticeRoot.WidgetTree=nt
    local border=StaticConstructObject(StaticFindObject("/Script/UMG.Border"),nt)
    nt.RootWidget=border; border:SetBrushColor({R=.025,G=.15,B=.17,A=.97})
    border:SetPadding({Left=16,Top=12,Right=16,Bottom=12})
    local text=StaticConstructObject(StaticFindObject("/Script/UMG.TextBlock"),nt)
    local scale=math.max(.5,layout:GetViewportScale(pc))
    local font=text.Font; font.Size=math.floor(15/scale); text:SetFont(font)
    text:SetColorAndOpacity({SpecifiedColor={R=.9,G=1,B=.96,A=1},ColorUseRule=0})
    text:SetAutoWrapText(true)
    text:SetText(FText(preview and "RoweRC update notification preview\nPreview only - no update is pending. Driving input stays active."
        or "RoweRC "..version.." downloaded and ready\nClose and reopen RC Tandems to install. F8 for details."))
    border:SetContent(text)
    noticeRoot:SetVisibility(3) -- No hit testing: keep driving and menu input unchanged.
    noticeRoot:AddToViewport(9001)
    noticeRoot:SetDesiredSizeInViewport({X=510/scale,Y=95/scale})
    noticeRoot:SetAlignmentInViewport({X=1,Y=0})
    local viewport=layout:GetViewportSize(pc)
    noticeRoot:SetPositionInViewport({X=viewport.X-24,Y=24},true)
    noticeVersion=version
end
function UI.PreviewUpdateNotice() previewUntil=os.time()+10 end
function UI.Close()
    if valid(root) then root:RemoveFromParent() end
    local pc = UE.GetPlayerController()
    if valid(pc) then
        local lib = StaticFindObject("/Script/UMG.Default__WidgetBlueprintLibrary")
        if wasDriving then lib:SetInputMode_GameOnly(pc,false)
        else lib:SetInputMode_GameAndUIEx(pc,nil,0,false,false) end
        pc.bShowMouseCursor = oldCursor
        if wasDriving and valid(pc.Pawn) then pc.Pawn:ResumeDrive() end
    end
    UI.Invalidate()
end
function UI.Open()
    if UI.open then UI.Close(); return end
    local pc = UE.GetPlayerController()
    if not valid(pc) or not valid(pc.Pawn) then return end
    local lib = StaticFindObject("/Script/UMG.Default__WidgetBlueprintLibrary")
    local layout = StaticFindObject("/Script/UMG.Default__WidgetLayoutLibrary")
    viewportScale = math.max(.5, layout:GetViewportScale(pc))
    local class = StaticFindObject("/Script/UMG.UserWidget")
    root = lib:Create(pc, class, pc)
    if not valid(root) then root = StaticConstructObject(class, UE.GetGameInstance()) end
    assert(valid(root), "Could not construct RoweRC menu")
    tree = StaticConstructObject(StaticFindObject("/Script/UMG.WidgetTree"), root)
    root.WidgetTree = tree
    local border = make("Border"); tree.RootWidget = border
    border:SetBrushColor({R=.025,G=.055,B=.075,A=.98})
    border:SetPadding({Left=24,Top=15,Right=24,Bottom=15})
    local frame=make("VerticalBox");border:SetContent(frame)
    add(frame,label("ROWE RC",24))
    add(frame,label("F8 to close",12))
    local tabs=make("HorizontalBox");add(frame,tabs)
    for _,tab in ipairs({{'online','Online'},{'car','Car'},{'maps','Maps'}}) do
        local key=tab[1]
        local column=make("VerticalBox");tabs:AddChildToHorizontalBox(column):SetPadding({Left=0,Top=0,Right=12,Bottom=0})
        tabButtons[key]=button(column,tab[2],function() chooseTab(key) end)
    end
    statusLabel=add(frame,label("Connecting to Steam...",12))
    local area=make("SizeBox");area:SetHeightOverride(470/viewportScale);add(frame,area)
    local scroll=make("ScrollBox");area:SetContent(scroll)
    for _,key in ipairs({'online','car','maps'}) do panels[key]=make("VerticalBox");scroll:AddChild(panels[key]) end
    local car=panels.car
    add(car,label("BODY SHELL",18))
    shellLabel=add(car,label("Game shell",14))
    for _,option in ipairs(Shells.options) do
        local id=option.id
        button(car,"Use "..option.name,function()
            local controller=UE.GetPlayerController()
            local ok,error=pcall(Shells.select,controller.Pawn,id)
            if not ok then Shells.error=tostring(error):gsub('^.-:%d+: ','') end
        end)
    end
    button(car,"Use game shell",function()
        local controller=UE.GetPlayerController()
        local ok,error=pcall(Shells.select,controller.Pawn,'stock')
        if not ok then Shells.error=tostring(error):gsub('^.-:%d+: ','') end
    end)
    add(car,label("Keeps your body color. Friends need the same shell pack installed.",12))
    add(car,label("RIM FINISH",18))
    rimLabel=add(car,label("Rim metallic",13))
    rimSlider=make("Slider")
    rimSlider:SetMinValue(0); rimSlider:SetMaxValue(1); rimSlider:SetStepSize(.01)
    rimLast=Rims.value(); rimSlider:SetValue(rimLast)
    add(car,rimSlider)
    add(car,label("Nonmetallic 0   -   Metallic 1  /  All rims",12))
    button(car,"Use game rim finish",function()
        local ok,error=pcall(Rims.stock)
        if ok then rimLast=Rims.current or 0; rimSlider:SetValue(rimLast)
        else rimLabel:SetText(FText(tostring(error))) end
    end)
    local box=panels.online
    add(box,label("PLAY TOGETHER",18))
    peerLabel=add(box,label("",12))
    onlineMapLabel=add(box,label("Track: loading...",14))
    hostButton=button(box,"Host friends",function() if maps[selected] then request("host",maps[selected].id) end end)
    publicButton=button(box,"Host public",function() if maps[selected] then request("host-public",maps[selected].id) end end)
    codeLabel=add(box,label("Lobby code: -",14))
    copyButton=button(box,"Copy lobby code",function() request("copy") end)
    joinInput=make("EditableTextBox");joinInput:SetHintText(FText("Friend's lobby code"))
    joinInput.WidgetStyle.TextStyle.Font.Size=math.floor(15/viewportScale)
    joinInput:SetForegroundColor({SpecifiedColor={R=.04,G=.10,B=.13,A=1},ColorUseRule=0});add(box,joinInput)
    joinButton=button(box,"Join friend",function() request("join",joinInput:GetText():ToString()) end)
    leaveButton=button(box,"Leave lobby",function() request("leave") end)
    contactLabel=add(box,label("Car contact: off",12))
    contactButton=button(box,"Toggle car contact (experimental)",function() request("contact") end)
    add(box,label("PUBLIC LOBBIES",18))
    browserLabel=add(box,label("Refresh to find Steam lobbies",12))
    button(box,"Refresh Steam lobbies",function() request("browse") end)
    for index=1,6 do
        local w=button(box,"",function()
            local lobby=lobbies[(browserPage-1)*6+index]
            if role=="idle" and lobby and lobby.problem=="" then request("join",lobby.id) end
        end)
        browserRows[index]=w; w:GetContent():SetAutoWrapText(true); w:SetVisibility(1)
    end
    local pages=make("HorizontalBox"); add(box,pages);browserPager=pages
    local previous,nextPage=make("VerticalBox"),make("VerticalBox")
    pages:AddChildToHorizontalBox(previous):SetPadding({Left=0,Top=0,Right=16,Bottom=0}); pages:AddChildToHorizontalBox(nextPage)
    button(previous,"< Lobby page",function() browserPage=math.max(1,browserPage-1) end)
    button(nextPage,"Lobby page >",function() browserPage=math.min(math.max(1,math.ceil(#lobbies/6)),browserPage+1) end)
    box=panels.maps
    add(box,label("TRACKS",18))
    mapLabel = add(box, label("Track: loading catalog...", 16))
    local row = make("HorizontalBox"); add(box, row)
    local left, right = make("VerticalBox"), make("VerticalBox")
    row:AddChildToHorizontalBox(left):SetPadding({Left=0,Top=0,Right=16,Bottom=0})
    row:AddChildToHorizontalBox(right)
    hostControls[#hostControls+1] = button(left, "< Previous track", function() if role~="guest" and #maps>0 then selected=(selected-2)%#maps+1 end end)
    hostControls[#hostControls+1] = button(right, "Next track >", function() if role~="guest" and #maps>0 then selected=selected%#maps+1 end end)
    mapButton = button(box, "Load / change host map", function() if role~="guest" and maps[selected] then request("map", maps[selected].id); UI.Close() end end)
    hostControls[#hostControls+1] = mapButton
    button(box, "Reload custom maps", function() request("refresh") end)
    add(box,label("The host chooses the track; guests follow automatically. Put map ZIPs in %LOCALAPPDATA%/RoweRC/Maps/Inbox.",12))
    add(box,label("MOD UPDATE",18))
    updateLabel = add(box, label("Checking mod updates...", 12))
    button(box, "Check for mod update", function() request("update") end)
    chooseTab(currentTab)
    root:AddToViewport(9000)
    root:SetDesiredSizeInViewport({X=700/viewportScale,Y=670/viewportScale})
    root:SetAlignmentInViewport({X=.5,Y=.5})
    local viewport = layout:GetViewportSize(pc)
    root:SetPositionInViewport({X=viewport.X/2,Y=viewport.Y/2},true)
    oldCursor = pc.bShowMouseCursor; pc.bShowMouseCursor = true
    wasDriving = pc.Pawn.bDriving
    if wasDriving then pc.Pawn:PauseDrive() end
    lib:SetInputMode_UIOnlyEx(pc, root, 0, false)
    UI.open = true
    request("browse")
end
function UI.Update()
    local pc=UE.GetPlayerController()
    if valid(pc) and valid(pc.Pawn) then
        local now=StaticFindObject("/Script/Engine.Default__GameplayStatics"):GetRealTimeSeconds(pc.Pawn)
        local ok,error=pcall(Rims.update,pc.Pawn,function(pawn,class,callback)
            for _,source in ipairs(FindAllOf(class) or {}) do
                if valid(source) and valid(source:GetOuter()) and source:GetOuter():GetAddress()==pawn:GetAddress() then callback(source) end
            end
        end,now)
        if not ok and valid(rimLabel) then rimLabel:SetText(FText("Rim finish: "..tostring(error):sub(1,160))) end
    end
    local text = read("ui-state.tsv")
    local time=tonumber(text:match("time\t(%d+)")) or 0
    if previewUntil and os.time()<previewUntil then updateNotice("0.1.6",true)
    else updateNotice(os.time()-time<=4 and text:match("update%-ready\t([^\r\n]*)") or nil) end
    if not UI.open or not valid(root) then return end
    shellLabel:SetText(FText("Shell: "..(Shells.error or Shells.status or 'Game shell')))
    local metallic=rimSlider:GetValue()
    if math.abs(metallic-rimLast)>.004 then
        local ok,error=pcall(Rims.set,metallic)
        if ok then rimLast=Rims.value() else rimLabel:SetText(FText(tostring(error))) end
    end
    if Rims.count and Rims.count>0 then rimLabel:SetText(FText(string.format("Rim metallic: %.2f",Rims.value())))
    else rimLabel:SetText(FText("Rim metallic: waiting for the car's rims")) end
    local heartbeat = 0
    local activeMap = ""
    local newMaps, newLobbies = {}, {}
    local peerLines={}
    for line in text:gmatch("[^\r\n]+") do
        local k, value, extra = line:match("^([^\t]+)\t([^\t]*)\t?(.*)$")
        if k == "browser" then browserLabel:SetText(FText(value))
        elseif k == "lobby" then
            local caption,problem=extra:match("^([^\t]*)\t(.*)$")
            newLobbies[#newLobbies+1]={id=value,caption=caption or extra,problem=problem or ""}
        elseif k == "role" then role = value
        elseif k == "peer" then peerLines[#peerLines+1]=value
        elseif k == "contact" then contactEnabled=value=="1"
        elseif k == "update" then updateLabel:SetText(FText(value))
        elseif k == "active" then activeMap = value
        elseif k == "time" then heartbeat = tonumber(value) or 0
        elseif k == "status" then statusLabel:SetText(FText(value:gsub(' — mod %S+, App ID %d+','')))
        elseif k == "code" then codeLabel:SetText(FText("Lobby code: " .. (value ~= "" and value or "-")))
        elseif k == "map" then newMaps[#newMaps+1] = {id=value,name=extra} end
    end
    if os.time() - heartbeat > 4 then statusLabel:SetText(FText("Steam helper offline. Start RoweRC.exe to enable hosting and maps.")) end
    peerLabel:SetText(FText(table.concat(peerLines,"\n")))
    peerLabel:SetVisibility(#peerLines>0 and 0 or 1)
    contactButton:SetVisibility(role=="host" and 0 or 1)
    contactLabel:SetVisibility(role~='idle' and 0 or 1)
    local contactError=read("contact-error.txt")
    contactLabel:SetText(FText(contactEnabled and ("Car contact: experimental ON - body hull + tires"..(contactError~="" and "\nCollider: "..contactError:sub(1,180) or "")) or "Car contact: OFF - host can enable it for this lobby"))
    lobbies=newLobbies
    browserPage=math.max(1,math.min(browserPage,math.max(1,math.ceil(#lobbies/6))))
    browserPager:SetVisibility(#lobbies>6 and 0 or 1)
    for i,w in ipairs(browserRows) do
        local lobby=lobbies[(browserPage-1)*6+i]
        w:SetVisibility(lobby and 0 or 1)
        if lobby then
            w:GetContent():SetText(FText((role=="idle" and lobby.problem=="" and "Join | " or "")..lobby.caption..(lobby.problem~="" and " | "..lobby.problem or "")))
            w:SetIsEnabled(role=="idle" and lobby.problem=="")
        end
    end
    if #newMaps > 0 then maps = newMaps end
    if role == "guest" then for index, map in ipairs(maps) do if map.id == activeMap then selected = index; break end end end
    for _, control in ipairs(hostControls) do if valid(control) then control:SetVisibility(role=="guest" and 1 or 0) end end
    hostButton:SetVisibility(role=="idle" and 0 or 1)
    publicButton:SetVisibility(role=="idle" and 0 or 1)
    joinButton:SetVisibility(role=="idle" and 0 or 1)
    joinInput:SetVisibility(role=="idle" and 0 or 1)
    codeLabel:SetVisibility(role~="idle" and 0 or 1)
    copyButton:SetVisibility(role~="idle" and 0 or 1)
    leaveButton:SetVisibility(role~="idle" and 0 or 1)
    selected = math.max(1, math.min(selected, #maps))
    if maps[selected] then
        mapLabel:SetText(FText((role=="guest" and "Host's track: " or "Selected track: ")..maps[selected].name))
        onlineMapLabel:SetText(FText("Track: "..maps[selected].name..(role~='guest' and '  /  Change in Maps' or '')))
    end
end
RegisterHook("/Script/RCDriftSim.RCUIAction:ExecuteAction", function(context)
    local object = context:get()
    if UI.open and valid(object) then
        local action = actions[tostring(object:GetAddress())]
        if action then click(action) end
    end
end)
RegisterKeyBind(Key.F8, function() ExecuteInGameThread(function() local ok,e=pcall(UI.Open); if not ok then print("[RoweRC UI] "..tostring(e).."\n") end end) end)
RegisterKeyBind(Key.LEFT_MOUSE_BUTTON, function()
    if not UI.open then return end
    ExecuteInGameThread(function()
        if not UI.open then return end
        for _, b in ipairs(buttons) do
            if valid(b) and b:IsHovered() then local action=actions[tostring(b:GetAddress())]; if action then click(action) end; break end
        end
    end)
end)
return UI

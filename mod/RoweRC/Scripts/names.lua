local M={}
local function valid(o) return o and o:IsValid() end
function M.update(group,name,mathLib,controller,location)
    if not name or name=='' or not valid(group.root) then return end
    if not valid(group.nameLabel) then
        local class=StaticFindObject('/Script/Engine.TextRenderComponent')
        if not valid(class) then return end
        local transform=mathLib:MakeTransform({X=0,Y=0,Z=35},{Pitch=0,Yaw=0,Roll=0},{X=1,Y=1,Z=1})
        group.nameLabel=group.root:AddComponentByClass(class,false,transform,false)
        if not valid(group.nameLabel) then return end
        group.nameLabel:SetWorldSize(5)
        group.nameLabel:SetHorizontalAlignment(1)
        group.nameLabel:SetTextRenderColor({R=175,G=245,B=255,A=255})
        group.nameLabel:SetCollisionEnabled(0)
    end
    if group.steamName~=name then group.nameLabel:SetText(FText(name));group.steamName=name end
    if valid(controller) and valid(controller.PlayerCameraManager) then
        local camera=controller.PlayerCameraManager:GetCameraLocation()
        local position={X=location.X,Y=location.Y,Z=location.Z+35}
        group.nameLabel:K2_SetWorldRotation(mathLib:FindLookAtRotation(position,camera),false,{},true)
        local d=math.sqrt((camera.X-position.X)^2+(camera.Y-position.Y)^2+(camera.Z-position.Z)^2)
        local scale=math.max(.65,math.min(3,d/250))
        group.nameLabel:SetWorldSize(5*scale)
        group.nameLabel:SetVisibility(d<2500,false)
    end
end
return M

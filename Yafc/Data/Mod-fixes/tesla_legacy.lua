-- Remove the inaccessible tesla objects
local function clean(type)
    for name,_ in pairs(data.raw[type]) do
        if name:match("^tl%-basic%-tesla%-coil%-") or name:match("^tl%-tesla%-coil%-") then
            data.raw[type][name] = nil
        end
    end
end

clean("land-mine")
clean("beam")

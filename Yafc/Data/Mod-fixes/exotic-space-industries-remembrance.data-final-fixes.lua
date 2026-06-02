-- Remove the inputs from the dummy lab, to remove the vanilla science packs from the default milestones
-- (This has to happen after ESIR adds them, in its d-f-f.lua)
if data.raw.lab["ei-dummy-lab"] then
	data.raw.lab["ei-dummy-lab"].inputs = {}
end

return ...

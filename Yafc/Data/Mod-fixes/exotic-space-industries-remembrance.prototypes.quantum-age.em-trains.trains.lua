-- Remove the pseudo-fuels for the EM trains (This has to happen before the recycling recipes get added)
for name, _ in pairs(data.raw.item) do
	if name:match("^ei_emt%-fuel_") then
		data.raw.item[name] = nil
	end
end

return ...

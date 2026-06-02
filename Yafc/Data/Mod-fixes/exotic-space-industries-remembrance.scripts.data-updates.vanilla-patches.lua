-- Patch the output temperature for the vanilla molten-metal recipes, since we care about that (ESI:R recipes output 900 degree molten metals)
local function update(recipe)
	if recipe and recipe.results and recipe.results[1] then
		recipe.results[1].temperature = 900
	end
end

update(data.raw.recipe["molten-copper"])
update(data.raw.recipe["molten-iron"])
update(data.raw.recipe["molten-copper-from-lava"])
update(data.raw.recipe["molten-iron-from-lava"])

return ...

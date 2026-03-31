local mirror = data.raw.turret["chcs-heliostat-mirror"]
local tower = data.raw.reactor["chcs-solar-power-tower"]
local fluid = data.raw.fluid["chcs-solar-fluid"]

if mirror and tower and fluid then
    data.raw.turret["chcs-heliostat-mirror"] = nil
    mirror.type = "assembling-machine"
    mirror.fixed_recipe = "yafc-chcs-solar-intensity"
    mirror.energy_source = { type = "void" }
    mirror.crafting_speed = 1.1 / 600 -- max energy is at 600°C, and each mirror provides 1.1°C (Here, "°C" is the display, not a real temperature)
    -- Extra mirrors allow you to produce more power in the mornings and evenings, up to an extra ~1.3% on average. We ignore this.

    -- 1.1 and 2.0 provide different values for these fields.
    local towerPower = yafc.parse_power(tower.consumption)
    local fluidCapacity = yafc.parse_energy(fluid.heat_capacity)

    data:extend { mirror,
    {
        name = "yafc-chcs-solar-intensity",
        type = "recipe",
        category = "yafc",
        energy_required = 1,
        results = {{
            name = "chcs-solar-fluid",
            type = "fluid",
            amount = 1,
            -- Assume there's enough thermal mass to cover the nighttime demand.
            -- If there isn't enough thermal mass, the system will require additional (unmodelled) heat exchangers and a steam battery.
            -- Tower consumption is 1 fluid per second; produce fluid at (averagePower / heatCapacity)°C to get the desired power output.
            temperature = towerPower * .7 / fluidCapacity
        }}
    }}
end

return ...

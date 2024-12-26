using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Atmos.Piping.Unary.Components;
using Content.Server.Atmos;
using Content.Server.Atmos.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.NodeContainer;
using Content.Shared.Atmos.Piping;
using Content.Shared.Atmos;
using Content.Shared.CCVar;
using Content.Shared.Interaction;
using JetBrains.Annotations;
using Robust.Shared.Configuration;

namespace Content.Server.Atmos.EntitySystems;

public sealed class HeatExchangerSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    float tileLoss;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HeatExchangerComponent, AtmosDeviceUpdateEvent>(OnAtmosUpdate);

        // Getting CVars is expensive, don't do it every tick
        Subs.CVar(_cfg, CCVars.SuperconductionTileLoss, CacheTileLoss, true);
    }

    private void CacheTileLoss(float val)
    {
        tileLoss = val;
    }

    private void OnAtmosUpdate(EntityUid uid, HeatExchangerComponent comp, ref AtmosDeviceUpdateEvent args)
    {
        // make sure that the tile the device is on isn't blocked by a wall or something similar.
        if (args.Grid is {} grid
            && _transform.TryGetGridTilePosition(uid, out var tile)
            && _atmosphereSystem.IsTileAirBlocked(grid, tile))
        {
            return;
        }

        if (!_nodeContainer.TryGetNodes(uid, comp.InletName, comp.OutletName, out PipeNode? inlet, out PipeNode? outlet))
            return;

        var dt = args.dt;

		//i don't know why HEs have an in and an out, and you know what, i don't want to know - i'm sure it's due to some pipecode fuckery that will hurt my brain
		//whatever. let's just mix them together and pretend that this isn't the case.
		
		_atmosphereSystem.Merge(outlet.Air, inlet.Air); //place inlet into outlet.
		inlet.Air.Remove(inlet.Air.TotalMoles); //remove the inlet gasses
		//we are going to hold off on splitting the gasses back into their sides, because it means we only have to do the math on 1 gas mix.
		
		
		//first priority is to figure out convection.
		const float convection_coeff=.1f;
		var environment = _atmosphereSystem.GetContainingMixture(uid, true, true);
		if(environment!=null){
			float envT=environment.Temperature;
			float pipeT=outlet.Air.Temperature;
			
			// 1- 1/(   (moles+volume)/volume  )
			// math formula, where x is moles and v is volume: 1-\frac{1}{\frac{x+v}{v}}    
			//this is to simulate more moles making more heat transfer avalible.
			//this has no basis in reality.
			float env_convection_coef= 1f-(1f/((environment.TotalMoles+environment.Volume)/environment.Volume));
			if(envT>pipeT){ // env -> pipe
				float EnergyToConvect= convection_coeff*env_convection_coef*(envT-pipeT);
				float heatcap_env= _atmosphereSystem.GetHeatCapacity(environment, true);
				float heatcap_pipe= _atmosphereSystem.GetHeatCapacity(outlet.Air, true);
				
				if(heatcap_env>Atmospherics.MinimumHeatCapacity && heatcap_pipe>Atmospherics.MinimumHeatCapacity){
					outlet.Air.Temperature+= EnergyToConvect*dt/heatcap_pipe; //divide by heat capacity to get temp change
					environment.Temperature-=EnergyToConvect*dt/heatcap_env;
				}
			}else{ // pipe->env
				float EnergyToConvect= convection_coeff*env_convection_coef*(pipeT-envT);
				float heatcap_env= _atmosphereSystem.GetHeatCapacity(environment, true);
				float heatcap_pipe= _atmosphereSystem.GetHeatCapacity(outlet.Air, true);
				
				if(heatcap_env>Atmospherics.MinimumHeatCapacity && heatcap_pipe>Atmospherics.MinimumHeatCapacity){
					outlet.Air.Temperature-= EnergyToConvect*dt/heatcap_pipe;
					environment.Temperature+=EnergyToConvect*dt/heatcap_env;
				}
			}
		}
		
		
		//next up, simulate heat loss via radiation. we are assuming a perfect black body for this and also spherical cows.
		//this uses the Stefan–Boltzmann law
		const float surface_area=10f;
		if(environment!=null){
			float pipetemp=outlet.Air.Temperature;
			float envtemp=environment.Temperature;
			if (outlet.Air.Temperature<environment.Temperature){ // environment -> pipe
				float energy_radiated = Atmospherics.StefanBoltzmann*surface_area*(  MathF.Pow(envtemp ,4f)-MathF.Pow(pipetemp ,4f)  );
				float heatcap_env= _atmosphereSystem.GetHeatCapacity(environment, true);
				float heatcap_pipe= _atmosphereSystem.GetHeatCapacity(outlet.Air, true);
				
				if(heatcap_env>Atmospherics.MinimumHeatCapacity) environment.Temperature-=energy_radiated*dt/heatcap_env;
				if(heatcap_pipe>Atmospherics.MinimumHeatCapacity) outlet.Air.Temperature+=energy_radiated*dt/heatcap_pipe;
			}else{ // pipe -> environment
				float energy_radiated = Atmospherics.StefanBoltzmann*surface_area*(  MathF.Pow(pipetemp ,4f)-MathF.Pow(envtemp ,4f)  );
				float heatcap_env= _atmosphereSystem.GetHeatCapacity(environment, true);
				float heatcap_pipe= _atmosphereSystem.GetHeatCapacity(outlet.Air, true);
				
				if(heatcap_env>Atmospherics.MinimumHeatCapacity) environment.Temperature+=energy_radiated*dt/heatcap_env;
				if(heatcap_pipe>Atmospherics.MinimumHeatCapacity) outlet.Air.Temperature-=energy_radiated*dt/heatcap_pipe;
			}
		}else{ //assume space cooling.
			float pipetemp=outlet.Air.Temperature;
			if (outlet.Air.Temperature<Atmospherics.TCMB){ // space -> pipe
				float energy_radiated = Atmospherics.StefanBoltzmann*surface_area*(  MathF.Pow(Atmospherics.TCMB ,4f)-MathF.Pow(pipetemp ,4f)  );
				float heatcap_pipe= _atmosphereSystem.GetHeatCapacity(outlet.Air, true);
				
				if(heatcap_pipe>Atmospherics.MinimumHeatCapacity) outlet.Air.Temperature+=energy_radiated*dt/heatcap_pipe;
			}else{ // pipe -> space
				float energy_radiated = Atmospherics.StefanBoltzmann*surface_area*( MathF.Pow(pipetemp ,4f)- MathF.Pow(Atmospherics.TCMB ,4f)  );
				float heatcap_pipe= _atmosphereSystem.GetHeatCapacity(outlet.Air, true);
				
				if(heatcap_pipe>Atmospherics.MinimumHeatCapacity) outlet.Air.Temperature-=energy_radiated*dt/heatcap_pipe;
			}
		}
		
		//back to the fuckery that is inlets+outlets:
		_atmosphereSystem.Merge(inlet.Air, outlet.Air.Remove(outlet.Air.TotalMoles/2.0f)); //move half of the outlet back to the inlet.
    }
}

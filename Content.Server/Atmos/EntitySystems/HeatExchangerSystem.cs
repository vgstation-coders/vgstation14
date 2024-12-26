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
		
		if(!_nodeContainer.TryGetNode(uid,comp.PipeName, out PipeNode? pipe  ) ) return;
		
        var dt = args.dt;

		
		//first priority is to figure out convection.
		var environment = _atmosphereSystem.GetContainingMixture(uid, true, true);
		if(environment!=null){
			float envT=environment.Temperature;
			float pipeT=pipe.Air.Temperature;
			
			// (1- 1/(   (5*moles+volume)/volume  ))^.75
			// math formula, where x is moles and v is volume: \left(1-\frac{1}{\frac{5x+v}{v}}\right)^{.75}  
			//this is to simulate more moles making more heat transfer avalible.
			//this has no basis in reality.
			float env_convection_coef= 1f-(1f/((500f*environment.TotalMoles+environment.Volume)/environment.Volume));
			env_convection_coef=MathF.Pow(env_convection_coef,.75f);
			
			if(envT>pipeT){ // env -> pipe
				float EnergyToConvect= comp.convection_coeff*env_convection_coef*(envT-pipeT);
				float heatcap_env= _atmosphereSystem.GetHeatCapacity(environment, true);
				float heatcap_pipe= _atmosphereSystem.GetHeatCapacity(pipe.Air, true);
				
				if(heatcap_env>Atmospherics.MinimumHeatCapacity && heatcap_pipe>Atmospherics.MinimumHeatCapacity){
					pipe.Air.Temperature+= EnergyToConvect*dt/heatcap_pipe; //divide by heat capacity to get temp change
					environment.Temperature-=EnergyToConvect*dt/heatcap_env;
				}
			}else{ // pipe->env
				float EnergyToConvect= comp.convection_coeff*env_convection_coef*(pipeT-envT);
				float heatcap_env= _atmosphereSystem.GetHeatCapacity(environment, true);
				float heatcap_pipe= _atmosphereSystem.GetHeatCapacity(pipe.Air, true);
				
				if(heatcap_env>Atmospherics.MinimumHeatCapacity && heatcap_pipe>Atmospherics.MinimumHeatCapacity){
					pipe.Air.Temperature-= EnergyToConvect*dt/heatcap_pipe;
					environment.Temperature+=EnergyToConvect*dt/heatcap_env;
				}
			}
		}
		
		
		//next up, simulate heat loss via radiation. we are assuming a perfect black body for this and also spherical cows.
		//this uses the Stefan–Boltzmann law
		if(environment!=null){
			float pipetemp=pipe.Air.Temperature;
			float envtemp=environment.Temperature;
			if (pipe.Air.Temperature<environment.Temperature){ // environment -> pipe
				float energy_radiated = Atmospherics.StefanBoltzmann*comp.surface_area*(  MathF.Pow(envtemp ,4f)-MathF.Pow(pipetemp ,4f)  );
				float heatcap_env= _atmosphereSystem.GetHeatCapacity(environment, true);
				float heatcap_pipe= _atmosphereSystem.GetHeatCapacity(pipe.Air, true);
				
				if(heatcap_env>Atmospherics.MinimumHeatCapacity) environment.Temperature-=energy_radiated*dt/heatcap_env;
				if(heatcap_pipe>Atmospherics.MinimumHeatCapacity) pipe.Air.Temperature+=energy_radiated*dt/heatcap_pipe;
			}else{ // pipe -> environment
				float energy_radiated = Atmospherics.StefanBoltzmann*comp.surface_area*(  MathF.Pow(pipetemp ,4f)-MathF.Pow(envtemp ,4f)  );
				float heatcap_env= _atmosphereSystem.GetHeatCapacity(environment, true);
				float heatcap_pipe= _atmosphereSystem.GetHeatCapacity(pipe.Air, true);
				
				if(heatcap_env>Atmospherics.MinimumHeatCapacity) environment.Temperature+=energy_radiated*dt/heatcap_env;
				if(heatcap_pipe>Atmospherics.MinimumHeatCapacity) pipe.Air.Temperature-=energy_radiated*dt/heatcap_pipe;
			}
		}else{ //assume space cooling.
			float pipetemp=pipe.Air.Temperature;
			if (pipe.Air.Temperature<Atmospherics.TCMB){ // space -> pipe
				float energy_radiated = Atmospherics.StefanBoltzmann*comp.surface_area*(  MathF.Pow(Atmospherics.TCMB ,4f)-MathF.Pow(pipetemp ,4f)  );
				float heatcap_pipe= _atmosphereSystem.GetHeatCapacity(pipe.Air, true);
				
				if(heatcap_pipe>Atmospherics.MinimumHeatCapacity) pipe.Air.Temperature+=energy_radiated*dt/heatcap_pipe;
			}else{ // pipe -> space
				float energy_radiated = Atmospherics.StefanBoltzmann*comp.surface_area*( MathF.Pow(pipetemp ,4f)- MathF.Pow(Atmospherics.TCMB ,4f)  );
				float heatcap_pipe= _atmosphereSystem.GetHeatCapacity(pipe.Air, true);
				
				if(heatcap_pipe>Atmospherics.MinimumHeatCapacity) pipe.Air.Temperature-=energy_radiated*dt/heatcap_pipe;
			}
		}
		
    }
}

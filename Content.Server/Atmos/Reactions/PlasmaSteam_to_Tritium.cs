using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;

using Content.Server.Atmos.GasReactionHelpers;

namespace Content.Server.Atmos.Reactions
{
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class TritiumProduction : IGasReactionEffect
    {
        public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
        {
			const float minimum_temp = Atmospherics.T0C+100f; //min temp for the reaction to occur
			const float peak_temp = Atmospherics.T0C+250f; // the temp where the reaction peaks in speed.
			const float maximum_temp = Atmospherics.T0C+500f; //max temp for it to occur
			const float energy_per_mole_transmuted=-50e3f; // 50 kJ per mole. endothermic.
			
			float currenttemp=mixture.Temperature;
			float totalsystemenergy= atmosphereSystem.GetHeatCapacity(mixture, true)*currenttemp;
			float plas = mixture.GetMoles(Gas.Plasma);
			float steam = mixture.GetMoles(Gas.WaterVapor);
			float trt = mixture.GetMoles(Gas.Tritium);
			float ox = mixture.GetMoles(Gas.Oxygen);
			
			if (plas<=0.0f || steam<=0.0f){
				return ReactionResult.NoReaction;
			}
			
			float conversionratio=1.0f; 
			
			
			conversionratio*=GasReactionHelperFunctions.reaction_mult_finite_band(currenttemp, minimum_temp, peak_temp, maximum_temp);
			conversionratio*=GasReactionHelperFunctions.reaction_mult_2gas_idealratio(.1f, steam,plas); //10-90. this is to slow it.
			conversionratio*= (plas+steam)/(mixture.TotalMoles-(ox+trt)*.5f);
			
			if(conversionratio>0.0f){
				float molesconvert = MathF.Min(steam,plas/9.0f);
				
				totalsystemenergy-=molesconvert*energy_per_mole_transmuted/heatScale;
				
				mixture.AdjustMoles(Gas.WaterVapor,  -molesconvert  ); //H2O
				mixture.AdjustMoles(Gas.Oxygen,  molesconvert*.5f  ); //O
				mixture.AdjustMoles(Gas.Tritium,  molesconvert  ); //H2
				
				float newheatcap=atmosphereSystem.GetHeatCapacity(mixture, true); //adjust for new heat
				if (newheatcap > Atmospherics.MinimumHeatCapacity){
					mixture.Temperature=totalsystemenergy/newheatcap;
				}
				
				return ReactionResult.Reacting;
			}
			return ReactionResult.NoReaction;
			
		}
	}
}
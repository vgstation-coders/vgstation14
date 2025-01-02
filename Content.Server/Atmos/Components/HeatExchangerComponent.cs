namespace Content.Server.Atmos.Components;

[RegisterComponent]
public sealed partial class HeatExchangerComponent : Component
{

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("pipe")]
    public string PipeName { get; set; } = "pipe";

	
	/// <summary>
    /// Thermal convection multiplier. 0.0-1.0
    /// </summary>
	[ViewVariables(VVAccess.ReadWrite)]
    [DataField("convection efficiency")]
    public float convection_coeff { get; set; } = .5f;
	
	
	/// <summary>
    /// the simulated surface area for thermal radiation. >0.0
    /// </summary>
	[ViewVariables(VVAccess.ReadWrite)]
    [DataField("radiation area")]
    public float surface_area { get; set; } = 3f;

}


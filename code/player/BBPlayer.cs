using Sandbox;


partial class BBPlayer : Player
{

	[ConVar.Replicated]
	public static int oitc_max_ammo_held { get; set; } = 7;

	[Net]
	public int PistolAmmo { get; private set; } = 1;

	private DamageInfo lastDamage;

	/// <summary>
	/// The clothing container is what dresses the citizen
	/// </summary>
	public ClothingContainer Clothing = new();
	public BBPlayer()
	{
		Inventory = new Inventory( this );
	}

	/// <summary>
	/// Initialize using this client
	/// </summary>
	public BBPlayer( IClient cl ) : this()
	{
		// Load clothing from client data
		Clothing.LoadFromClient( cl );
	}

	public override void Spawn()
	{
		base.Spawn();
		FlashlightEntity = new SpotLightEntity
		{
			Enabled = false,
			DynamicShadows = true,
			Range = 3200f,
			Falloff = 0.3f,
			LinearAttenuation = 0.3f,
			Brightness = 5f,
			Color = Color.FromBytes( 200, 200, 200, 230 ),
			InnerConeAngle = 9,
			OuterConeAngle = 32,
			FogStrength = 1.0f,
			Owner = this,
			EnableViewmodelRendering = true,
			LightCookie = Texture.Load( "materials/effects/lightcookie.vtex" )
		};
		FlashlightPosOffset = 30f;
	}

	public void SetCookieFlashlightCookie()
	{
		Game.AssertServer();
		FlashlightEntity = new SpotLightEntity
		{
			Enabled = false,
			DynamicShadows = true,
			Range = 3200f,
			Falloff = 0.3f,
			LinearAttenuation = 0.3f,
			Brightness = 5f,
			Color = Color.FromBytes( 200, 200, 200, 230 ),
			InnerConeAngle = 9,
			OuterConeAngle = 32,
			FogStrength = 1.0f,
			Owner = this,
			EnableViewmodelRendering = true,
			LightCookie = Texture.Load( "textures/cookie.vtex" )
		};
		FlashlightPosOffset = 30f;
	}

	public override void ClientSpawn()
	{
		base.ClientSpawn();
		Game.AssertClient();
	}

	public override void Respawn()
	{
		SetModel("models/humans/male.vmdl");

		var useLightSkinTone = Game.Random.Int(0, 1) == 1;
		if (useLightSkinTone)
			SetMaterialGroup("skin1");

		var head = new AnimatedEntity();
		head.Model = Model.Load(useLightSkinTone ? "models/humans/heads/adam/adam.vmdl" : "models/humans/heads/frank/frank.vmdl");
		head.EnableHideInFirstPerson = true;
		head.EnableShadowInFirstPerson = true;
		head.SetParent(this, true);

		Controller = new WalkController();

		EnableAllCollisions = true;
		EnableDrawing = true;
		EnableHideInFirstPerson = true;
		EnableShadowInFirstPerson = true;

		//Clothing.DressEntity( this );
		Inventory = new Inventory( this );

		Inventory.Add( new WeaponFists(), false );
		Inventory.Add( new WeaponOITCPistol(), true );

		FlashlightBatteryCharge = 100f;

		//Just to make sure no one gets stuck with an empty pistol.
		if ( PistolAmmo <= 0 )
		{
			SwitchToFists();
		}

		base.Respawn();
	}

	public override void Simulate( IClient cl )
	{
		base.Simulate( cl );

		if ( LifeState != LifeState.Alive )
			return;

		TickPlayerUse();
		SimulateActiveChild( cl, ActiveChild );
		SimulateAnimation((WalkController)Controller);

		if ( Game.IsClient ) 
			return;

		FlashlightSimulate();

	}

	public override void FrameSimulate( IClient cl )
	{
		base.FrameSimulate( cl );

		Camera.Position = EyePosition;
		Camera.Rotation = EyeRotation;
		Camera.FieldOfView = 90f;
		Camera.FirstPersonViewer = this;

		FlashlightFrameSimulate();
	}

	private void SimulateAnimation(WalkController controller)
	{
		if (controller == null)
			return;

		// where should we be rotated to
		var turnSpeed = 0.02f;

		Rotation rotation;

		// If we're a bot, spin us around 180 degrees.
		if (Client.IsBot)
			rotation = ViewAngles.WithYaw(ViewAngles.yaw + 180f).ToRotation();
		else
			rotation = ViewAngles.ToRotation();

		var idealRotation = Rotation.LookAt(rotation.Forward.WithZ(0), Vector3.Up);
		Rotation = Rotation.Slerp(Rotation, idealRotation, controller.WishVelocity.Length * Time.Delta * turnSpeed);
		Rotation = Rotation.Clamp(idealRotation, 45.0f, out var shuffle); // lock facing to within 45 degrees of look direction

		var animHelper = new CitizenAnimationHelper(this);

		animHelper.WithWishVelocity(controller.WishVelocity);
		animHelper.WithVelocity(Velocity);
		animHelper.WithLookAt(EyePosition + EyeRotation.Forward * 100.0f, 1.0f, 1.0f, 0.5f);
		animHelper.AimAngle = rotation;
		animHelper.FootShuffle = shuffle;
		animHelper.DuckLevel = MathX.Lerp(animHelper.DuckLevel, controller.HasTag("ducked") ? 1 : 0, Time.Delta * 10.0f);
		animHelper.VoiceLevel = (Game.IsClient && Client.IsValid()) ? Client.Voice.LastHeard < 0.5f ? Client.Voice.CurrentLevel : 0.0f : 0.0f;
		animHelper.IsGrounded = GroundEntity != null;
		animHelper.IsSitting = controller.HasTag("sitting");
		animHelper.IsNoclipping = controller.HasTag("noclip");
		animHelper.IsClimbing = controller.HasTag("climbing");
		animHelper.IsSwimming = this.GetWaterLevel() >= 0.5f;
		animHelper.IsWeaponLowered = false;

		if (controller.HasEvent("jump"))
			animHelper.TriggerJump();

		//if (ActiveCarriable != _lastActiveCarriable)
		//	animHelper.TriggerDeploy();

		//if (ActiveCarriable is not null)
		//	ActiveCarriable.SimulateAnimator(animHelper);
		//else
		//{
		//	animHelper.HoldType = CitizenAnimationHelper.HoldTypes.None;
		//	animHelper.AimBodyWeight = 0.5f;
		//}
	}

	public override void OnKilled()
	{
		base.OnKilled();

		EnableDrawing = false;
		Controller = null;
		EnableAllCollisions = false;
		EnableDrawing = false;

		Inventory.DeleteContents();
	}

	public override void TakeDamage( DamageInfo info )
	{
		lastDamage = info;
		base.TakeDamage( info );
	}

	//could use setter/getters but this seems more clear.
	public void AwardAmmo( int amt )
	{
		Game.AssertServer();

		if ( PistolAmmo > oitc_max_ammo_held ) return;
		if ( PistolAmmo + amt > oitc_max_ammo_held )
		{
			PistolAmmo = oitc_max_ammo_held;
		}
		else
		{
			PistolAmmo += amt;
		}

		//If we are being rewarded ammo and we currently have out fists out
		//by force, switch back to pistol.
		if ( Inventory.Active is WeaponFists )
		{
			SwitchToPistol();
		}
	}

	public void RemoveAmmo( int amtToRemove )
	{
		PistolAmmo -= amtToRemove;
		if ( PistolAmmo <= 0 )
		{
			SwitchToFists();
		}

	}

	//more human readable functions, considering the scope of this mode, its fine.
	public void SwitchToFists()
	{
		Inventory.SetActiveSlot( 0, false );
	}

	public void SwitchToPistol()
	{
		Inventory.SetActiveSlot( 1, false );
	}

	[ClientRpc]
	public void PlayClientSound( string snd )
	{
		PlaySound( snd );
	}

}

using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TTT;

public partial class Player
{
	public const float MaxHealth = 100f;

	[Net]
	public TimeSince TimeSinceDeath { get; private set; }

	public float DistanceToAttacker { get; set; }

	/// <summary>
	/// It's always better to use this than <see cref="Entity.LastAttackerWeapon"/>
	/// because the weapon may be invalid.
	/// </summary>
	public CarriableInfo LastAttackerWeaponInfo { get; private set; }

	public DamageInfo LastDamage { get; private set; }

	public new float Health
	{
		get => base.Health;
		set => base.Health = Math.Clamp( value, 0, MaxHealth );
	}

	public new Entity LastAttacker
	{
		get => base.LastAttacker;
		set
		{
			// If anyone uses a prop to kill someone.
			if ( value is Prop prop && prop.LastAttacker is Player propAttacker )
				base.LastAttacker = propAttacker;
			else
				base.LastAttacker = value;
		}
	}

	/// <summary>
	/// Whether or not they were killed by another Player.
	/// This includes if the Player used a prop to kill them.
	/// </summary>
	public bool KilledByPlayer => LastAttacker is Player && LastAttacker != this;
	/// <summary>
	/// The base/start karma is determined once per round and determines the player's
	/// damage factor. It is networked and shown on clients.
	/// </summary>
	[Net]
	public float BaseKarma { get; set; }
	/// <summary>
	/// The damage factor scales how much damage the player deals, so if it is 0.9
	/// then the player only deals 90% of his original damage.
	/// </summary>
	public float DamageFactor { get; set; } = 1f;
	/// <summary>
	/// If a player damages another team member that is "clean" (no active timer),
	/// they'll end up with time being tacked onto this timer. A player will receive a
	/// karma bonus for remaining "clean" (having no active timer) at the end of the round.
	/// </summary>
	public TimeUntil TimeUntilClean { get; set; } = 0f;
	/// <summary>
	/// The active karma starts equal to the base karma, but is updated as the
	/// player damages/kills others. When a player damages/kills another, the
	/// active karma is used to determine his karma penalty.
	/// </summary>
	public float ActiveKarma { get; set; }

	public override void OnKilled()
	{
		Game.AssertServer();

		LifeState = LifeState.Dead;
		Status = PlayerStatus.MissingInAction;
		TimeSinceDeath = 0;

		if ( KilledByPlayer )
			((Player)LastAttacker).PlayersKilled.Add( this );

		Corpse = new Corpse( this );
		RemoveAllDecals();
		StopUsing();

		EnableAllCollisions = false;
		EnableDrawing = false;
		EnableTouch = false;

		Inventory.DropAll();
		DeleteFlashlight();
		DeleteItems();

		if ( !LastDamage.IsSilent() )
			PlaySound( "player_death" );

		Event.Run( GameEvent.Player.Killed, this );
		GameManager.Current.State.OnPlayerKilled( this );

		ClientOnKilled( this );
	}

	private void ClientOnKilled()
	{
		Game.AssertClient();

		if ( IsLocalPawn )
		{
			CurrentChannel = Channel.Spectator;

			if ( Corpse.IsValid() )
				CameraMode.Current = new FollowEntityCamera( Corpse );

			ClearButtons();
		}

		DeleteFlashlight();
		Event.Run( GameEvent.Player.Killed, this );
	}

	public override void TakeDamage( DamageInfo info )
	{
		Game.AssertServer();

		if ( !IsAlive )
			return;

		if ( info.Attacker is Prop && info.Attacker.Tags.Has( DamageTags.IgnoreDamage ) )
			return;

		if ( info.Attacker is Player attacker && attacker != this )
		{
			if ( GameManager.Current.State is not InProgress and not PostRound )
				return;

			if ( !info.HasTag( DamageTags.Slash ) )
				info.Damage *= attacker.DamageFactor;
		}

		if ( info.HasTag( DamageTags.Bullet ) )
		{
			info.Damage *= GetBulletDamageMultipliers( info );
			CreateBloodSplatter( info, 180f );
		}

		if ( info.HasTag( DamageTags.Slash ) )
			CreateBloodSplatter( info, 64f );

		if ( info.HasTag( DamageTags.Blast ) )
			Deafen( To.Single( this ), info.Damage.LerpInverse( 0, 60 ) );

		info.Damage = Math.Min( Health, info.Damage );

		LastAttacker = info.Attacker;
		LastAttackerWeapon = info.Weapon;
		LastAttackerWeaponInfo = (info.Weapon as Carriable)?.Info;
		LastDamage = info;

		Health -= info.Damage;
		Event.Run( GameEvent.Player.TookDamage, this );

		SendDamageInfo( To.Single( this ) );

		this.ProceduralHitReaction( info );

		if ( Health <= 0f )
			OnKilled();
	}

	public void SendDamageInfo( To to )
	{
		Game.AssertServer();

		SendDamageInfo
		(
			to,
			LastAttacker,
			LastDamage.Weapon,
			LastAttackerWeaponInfo,
			LastDamage.Damage,
			LastDamage.Tags?.ToArray(),
			LastDamage.Position,
			DistanceToAttacker
		);
	}

	private void CreateBloodSplatter( DamageInfo info, float maxDistance )
	{
		var trace = Trace.Ray( new Ray( info.Position, info.Force.Normal ), maxDistance )
			.Ignore( this )
			.Run();

		if ( !trace.Hit )
			return;

		var decal = ResourceLibrary.Get<DecalDefinition>( "decals/blood_splatter.decal" );
		Decal.Place( To.Everyone, decal, null, 0, trace.EndPosition - trace.Direction * 1f, Rotation.LookAt( trace.Normal ), Color.White );
	}

	private float GetBulletDamageMultipliers( DamageInfo info )
	{
		var damageMultiplier = 1f;

		if ( Perks.Has<Armor>() )
			damageMultiplier *= Armor.ReductionPercentage;

		if ( info.IsHeadshot() )
		{
			var weaponInfo = GameResource.GetInfo<WeaponInfo>( info.Weapon.ClassName );
			damageMultiplier *= weaponInfo?.HeadshotMultiplier ?? 2f;
		}
		else if ( info.Hitbox.HasAnyTags( "arm", "hand" ) )
		{
			damageMultiplier *= 0.55f;
		}

		return damageMultiplier;
	}

	private void ResetDamageData()
	{
		DistanceToAttacker = 0;
		LastAttacker = null;
		LastAttackerWeapon = null;
		LastAttackerWeaponInfo = null;
		LastDamage = default;
	}

	[ClientRpc]
	public static void Deafen( float strength )
	{
		Audio.SetEffect( "flashbang", strength, velocity: 20.0f, fadeOut: 4.0f * strength );
	}

	[ClientRpc]
	private void SendDamageInfo( Entity a, Entity w, CarriableInfo wI, float d, string[] tags, Vector3 p, float dTA )
	{
		var info = DamageInfo.Generic( 100f )
			.WithAttacker( a )
			.WithWeapon( w )
			.WithPosition( p );

		info.Tags = new HashSet<string>( tags ?? Array.Empty<string>() );

		DistanceToAttacker = dTA;
		LastAttacker = a;
		LastAttackerWeapon = w;
		LastAttackerWeaponInfo = wI;
		LastDamage = info;

		if ( IsLocalPawn )
			Event.Run( GameEvent.Player.TookDamage, this );
	}

	[ClientRpc]
	public static void ClientOnKilled( Player player )
	{
		if ( !player.IsValid() )
			return;

		player.ClientOnKilled();
	}
}

using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TTT;

public enum FireMode
{
	Automatic = 0,
	Semi = 1,
	Burst = 2
}

[Title( "Weapon" ), Icon( "sports_martial_arts" )]
public abstract partial class Weapon : Carriable
{
	[Net, Predicted]
	public int AmmoClip { get; protected set; }

	[Net, Predicted]
	public int ReserveAmmo { get; protected set; }

	[Net, Local, Predicted]
	public bool IsReloading { get; protected set; }

	[Net, Local, Predicted]
	public TimeSince TimeSincePrimaryAttack { get; protected set; }

	[Net, Local, Predicted]
	public TimeSince TimeSinceSecondaryAttack { get; protected set; }

	[Net, Local, Predicted]
	public TimeSince TimeSinceReload { get; protected set; }

	public new WeaponInfo Info => (WeaponInfo)base.Info;
	public override string SlotText => $"{AmmoClip} + {ReserveAmmo + Owner?.AmmoCount( Info.AmmoType )}";
	private Vector2 RecoilOnShoot => new( Game.Random.Float( -Info.HorizontalRecoilRange, Info.HorizontalRecoilRange ), Info.VerticalRecoil );
	private Vector2 CurrentRecoil { get; set; } = Vector2.Zero;

	public override void Spawn()
	{
		base.Spawn();

		AmmoClip = Info.ClipSize;

		if ( Info.AmmoType == AmmoType.None )
			ReserveAmmo = Info.ReserveAmmo;
	}

	public override void ActiveStart( Player player )
	{
		base.ActiveStart( player );

		IsReloading = false;
		TimeSinceReload = 0;
	}

	public override void Simulate( IClient client )
	{
		if ( CanReload() )
		{
			Reload();
			return;
		}

		if ( !IsReloading )
		{
			if ( CanPrimaryAttack() )
			{
				using ( LagCompensation() )
				{
					TimeSincePrimaryAttack = 0;
					AttackPrimary();
				}
			}

			if ( CanSecondaryAttack() )
			{
				using ( LagCompensation() )
				{
					TimeSinceSecondaryAttack = 0;
					AttackSecondary();
				}
			}

			if ( Input.Down( InputButton.Run ) && Input.Pressed( InputButton.Drop ) )
				DropAmmo();
		}
		else if ( TimeSinceReload > Info.ReloadTime )
			OnReloadFinish();
	}

	public override void BuildInput()
	{
		base.BuildInput();

		if ( Owner is null )
			return;

		var oldPitch = Owner.ViewAngles.pitch;
		var oldYaw = Owner.ViewAngles.yaw;

		var recoil = Owner.ViewAngles;
		recoil.pitch -= CurrentRecoil.y * Time.Delta;
		recoil.yaw -= CurrentRecoil.x * Time.Delta;

		Owner.ViewAngles = recoil;

		CurrentRecoil -= CurrentRecoil
			.WithY( (oldPitch - Owner.ViewAngles.pitch) * Info.RecoilRecoveryScale )
			.WithX( (oldYaw - Owner.ViewAngles.yaw) * Info.RecoilRecoveryScale );
	}

	protected virtual bool CanPrimaryAttack()
	{
		if ( Info.FireMode == FireMode.Semi && !Input.Pressed( InputButton.PrimaryAttack ) )
			return false;
		else if ( Info.FireMode != FireMode.Semi && !Input.Down( InputButton.PrimaryAttack ) )
			return false;

		var rate = Info.PrimaryRate;
		if ( rate <= 0 )
			return true;

		return TimeSincePrimaryAttack > (1 / rate);
	}

	protected virtual bool CanSecondaryAttack()
	{
		if ( !Input.Pressed( InputButton.SecondaryAttack ) )
			return false;

		var rate = Info.SecondaryRate;
		if ( rate <= 0 )
			return true;

		return TimeSinceSecondaryAttack > (1 / rate);
	}

	protected virtual void AttackPrimary()
	{
		if ( AmmoClip == 0 )
		{
			DryFireEffects();
			PlaySound( Info.DryFireSound );

			return;
		}

		AmmoClip--;

		Owner.SetAnimParameter( "b_attack", true );
		ShootEffects();
		PlaySound( Info.FireSound );

		ShootBullet( Info.Spread, 1.5f, Info.Damage, 2.0f, Info.BulletsPerFire );
	}

	protected virtual void AttackSecondary() { }

	protected virtual bool CanReload()
	{
		if ( IsReloading )
			return false;

		if ( !Input.Pressed( InputButton.Reload ) )
			return false;

		if ( AmmoClip >= Info.ClipSize || (Owner.AmmoCount( Info.AmmoType ) <= 0 && ReserveAmmo <= 0) )
			return false;

		return true;
	}

	protected virtual void Reload()
	{
		if ( IsReloading )
			return;

		TimeSinceReload = 0;
		IsReloading = true;

		Owner.SetAnimParameter( "b_reload", true );
		ReloadEffects();
	}

	protected virtual void OnReloadFinish()
	{
		IsReloading = false;
		AmmoClip += TakeAmmo( Info.ClipSize - AmmoClip );
	}

	[ClientRpc]
	protected virtual void ShootEffects()
	{
		if ( !Info.MuzzleFlashParticle.IsNullOrEmpty() )
			Particles.Create( Info.MuzzleFlashParticle, EffectEntity, "muzzle" );

		ViewModelEntity?.SetAnimParameter( "fire", true );
		CurrentRecoil += RecoilOnShoot;
	}

	[ClientRpc]
	protected virtual void DryFireEffects()
	{
		ViewModelEntity?.SetAnimParameter( "dryfire", true );
	}

	[ClientRpc]
	protected virtual void ReloadEffects()
	{
		ViewModelEntity?.SetAnimParameter( "reload", true );
	}

	protected virtual void ShootBullet( float spread, float force, float damage, float bulletSize, int bulletCount )
	{
		// Seed rand using the tick, so bullet cones match on client and server
		Game.SetRandomSeed( Time.Tick );

		while ( bulletCount-- > 0 )
		{
			var forward = Owner.EyeRotation.Forward;
			forward += (Vector3.Random + Vector3.Random + Vector3.Random + Vector3.Random) * spread * 0.25f;
			forward = forward.Normal;

			foreach ( var trace in TraceBullet( Owner.EyePosition, Owner.EyePosition + forward * 20000f, bulletSize ) )
			{
				trace.Surface.DoBulletImpact( trace );

				var fullEndPosition = trace.EndPosition + trace.Direction * bulletSize;

				if ( !Info.TracerParticle.IsNullOrEmpty() && trace.Distance > 200 )
				{
					var tracer = Particles.Create( Info.TracerParticle );
					tracer.SetPosition( 0, EffectEntity.Position );
					tracer.SetPosition( 1, trace.EndPosition );
				}

				if ( !Game.IsServer )
					continue;

				if ( !trace.Entity.IsValid() )
					continue;

				using ( Prediction.Off() )
				{
					OnHit( trace );

					if ( Info.Damage <= 0 )
						continue;

					var damageInfo = DamageInfo.FromBullet( trace.EndPosition, forward * 100f * force, damage )
						.UsingTraceResult( trace )
						.WithAttacker( Owner )
						.WithWeapon( this );

					if ( trace.Entity is Player player )
					{
						player.DistanceToAttacker = Vector3.DistanceBetween( Owner.Position, player.Position ).SourceUnitsToMeters();

						if ( Info.IsSilenced || damageInfo.IsHeadshot() )
							damageInfo.Tags.Add( "silent" );
					}

					trace.Entity.TakeDamage( damageInfo );
				}
			}
		}
	}

	/// <summary>
	/// Called when the bullet hits something, i.e the World or an entity.
	/// </summary>
	/// <param name="trace"></param>
	protected virtual void OnHit( TraceResult trace ) { }

	/// <summary>
	/// Does a trace from start to end, does bullet impact effects. Coded as an IEnumerable so you can return multiple
	/// hits, like if you're going through layers or ricocet'ing or something.
	/// </summary>
	protected IEnumerable<TraceResult> TraceBullet( Vector3 start, Vector3 end, float radius = 2.0f )
	{
		var traceResults = new List<TraceResult>();

		var underWater = Trace.TestPoint( start, "water" );

		var trace = Trace.Ray( start, end )
				.UseHitboxes()
				.WithAnyTags( "solid", "player", "glass", "interactable" )
				.Ignore( this )
				.Size( radius );

		//
		// If we're not underwater then we can hit water
		//
		if ( !underWater )
			trace = trace.WithAnyTags( "water" );

		var tr = trace.Run();

		if ( tr.Hit )
			traceResults.Add( tr );

		//
		// Let people shoot through glass
		//
		var hitGlass = Array.Find( tr.Tags, ( tag ) => tag == "glass" ) is not null;
		if ( hitGlass )
		{
			trace = Trace.Ray( tr.EndPosition, end )
				.UseHitboxes()
				.WithAnyTags( "solid", "player", "glass", "interactable" )
				.Ignore( tr.Entity )
				.Size( radius );

			tr = trace.Run();

			if ( tr.Hit )
				traceResults.Add( tr );
		}

		return traceResults;
	}

	protected void DropAmmo()
	{
		if ( Info.AmmoType == AmmoType.None || AmmoClip <= 0 )
			return;

		if ( Game.IsServer )
			Ammo.Drop( Owner, Info.AmmoType, AmmoClip );

		AmmoClip = 0;
	}

	protected int TakeAmmo( int ammo )
	{
		var available = Math.Min( Info.AmmoType == AmmoType.None ? ReserveAmmo : Owner.AmmoCount( Info.AmmoType ), ammo );

		if ( Info.AmmoType == AmmoType.None )
			ReserveAmmo -= available;
		else
			Owner.TakeAmmo( Info.AmmoType, available );

		return available;
	}

	public static float GetDamageFalloff( float distance, float damage, float start, float end )
	{
		if ( end > 0f )
		{
			if ( start > 0f )
			{
				if ( distance < start )
					return damage;

				var falloffRange = end - start;
				var difference = (distance - start);

				return Math.Max( damage - (damage / falloffRange) * difference, 0f );
			}

			return Math.Max( damage - (damage / end) * distance, 0f );
		}

		return damage;
	}
}

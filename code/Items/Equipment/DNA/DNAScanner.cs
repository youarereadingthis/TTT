using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TTT;

[Category( "Equipment" )]
[ClassName( "ttt_equipment_dnascanner" )]
[HideInEditor]
[Title( "DNA Scanner" )]
public partial class DNAScanner : Carriable
{
	[Net, Local]
	public IList<DNA> DNACollected { get; set; }

	// Waiting on https://github.com/Facepunch/sbox-issues/issues/1719
	// Unable to network "DNA" by itself due to s&box...
	[Net, Local]
	public int? SelectedId { get; set; }

	[Net, Local]
	public bool AutoScan { get; set; } = false;

	[Net, Local]
	private float Charge { get; set; } = MaxCharge;

	public override List<UI.BindingPrompt> BindingPrompts => new()
	{
		new( InputButton.PrimaryAttack, "Fetch DNA" ),
		new( InputButton.SecondaryAttack, !AutoScan ? "Scan" : string.Empty ),
		new( InputButton.View, "DNA Menu" )
	};
	public override string SlotText => $"{(int)Charge}%";
	public bool IsCharging => Charge < MaxCharge;

	private const float MaxCharge = 100f;
	private const float ChargePerSecond = 2.2f;
	private UI.WorldMarker _marker;

	public override void Simulate( IClient client )
	{
		if ( Input.Pressed( InputButton.PrimaryAttack ) )
			FetchDNA();

		if ( Input.Pressed( InputButton.SecondaryAttack ) )
			Scan();
	}

	public override void OnCarryDrop( Player dropper )
	{
		base.OnCarryDrop( dropper );

		if ( !dropper.IsLocalPawn )
			return;

		_marker?.Delete();
	}

	public void Scan()
	{
		if ( !Game.IsServer || IsCharging )
			return;

		var selectedDNA = FindSelectedDNA( SelectedId );
		if ( selectedDNA is null )
			return;

		var target = selectedDNA.GetTarget();
		if ( !target.IsValid() )
		{
			RemoveDNA( selectedDNA );
			UI.InfoFeed.AddEntry( To.Single( Owner ), "DNA not detected in area." );
			return;
		}

		var dist = Owner.Position.Distance( target.Position );
		Charge = Math.Max( 0, Charge - Math.Max( 4, dist / 25f ) );
		UpdateMarker( To.Single( Owner ), target.Position );
	}

	public void RemoveDNA( DNA dna )
	{
		Game.AssertServer();

		if ( dna.Id == SelectedId )
		{
			SelectedId = null;
			DeleteMarker( To.Single( Owner ) );
		}

		DNACollected.Remove( dna );
	}

	private void FetchDNA()
	{
		if ( !Game.IsServer )
			return;

		var trace = Trace.Ray( Owner.EyePosition, Owner.EyePosition + Owner.EyeRotation.Forward * Player.UseDistance )
			.Ignore( this )
			.Ignore( Owner )
			.WithTag( "interactable" )
			.Run();

		if ( !trace.Entity.IsValid() )
			return;

		if ( trace.Entity is Corpse corpse && !corpse.Player.IsConfirmedDead )
		{
			UI.InfoFeed.AddEntry( To.Single( Owner ), "Corpse must be identified to retrieve DNA sample." );
			return;
		}

		var samples = trace.Entity.Components.GetAll<DNA>();
		if ( !samples.Any() )
			return;

		var totalCollected = 0;
		foreach ( var dna in samples )
		{
			if ( dna.TimeUntilDecayed )
			{
				dna.Enabled = false;
				continue;
			}

			if ( !DNACollected.Contains( dna ) )
			{
				DNACollected.Add( dna );
				totalCollected += 1;
			}
		}

		if ( totalCollected > 0 )
			UI.InfoFeed.AddEntry( To.Single( Owner ), $"Collected {totalCollected} new DNA sample(s)." );
	}

	private DNA FindSelectedDNA( int? id )
	{
		if ( id is null )
			return null;

		foreach ( var sample in DNACollected )
			if ( sample.Id == id )
				return sample;

		return null;
	}

	[Event.Tick.Server]
	private void ServerTick()
	{
		if ( Owner is null )
			return;

		Charge = Math.Min( Charge + ChargePerSecond * Time.Delta, MaxCharge );

		if ( AutoScan )
			Scan();
	}

	[ClientRpc]
	private void UpdateMarker( Vector3 pos )
	{
		PlaySound( "dna-beep" );
		_marker?.Delete();
		_marker = new UI.WorldMarker
		(
			"/ui/dna-icon.png",
			() => $"{(Game.LocalPawn as Player).Position.Distance( pos ).SourceUnitsToMeters():n0}m",
			() => pos
		);
		UI.WorldPoints.Instance.AddChild( _marker );
	}

	[ClientRpc]
	private void DeleteMarker()
	{
		_marker?.Delete();
	}
}

public partial class DNA : EntityComponent
{
	// Waiting on https://github.com/Facepunch/sbox-issues/issues/1719
	[Net]
	public int Id { get; private set; }
	private static int _internalId = Game.Random.Int( 0, 500 );

	[Net]
	public string SourceName { get; private set; }

	public Player TargetPlayer { get; private set; }
	public TimeUntil TimeUntilDecayed { get; private set; }

	public DNA() { }

	public DNA( Player player )
	{
		TargetPlayer = player;
	}

	protected override void OnActivate()
	{
		if ( Game.IsClient )
			return;

		Id = _internalId++;

		switch ( Entity )
		{
			case Corpse corpse:
			{
				SourceName = $"{corpse.Player.SteamName}'s corpse";
				TimeUntilDecayed = MathF.Pow( 0.74803f * corpse.Player.DistanceToAttacker, 2 ) + 100;

				break;
			}
			default:
			{
				// Use: DisplayInfo.For( this ).Name
				SourceName = Entity.ClassName;
				TimeUntilDecayed = float.MaxValue; // Never should decay.

				break;
			}
		}
	}

	public Entity GetTarget()
	{
		if ( !TargetPlayer.IsValid() )
			return null;

		var decoyComponent = TargetPlayer.Components.Get<DecoyComponent>();
		if ( decoyComponent is not null && decoyComponent.Decoy.IsValid() )
			return decoyComponent.Decoy;

		return TargetPlayer.IsAlive ? TargetPlayer : TargetPlayer.Corpse;
	}

	[GameEvent.Round.Start]
	private void OnRolesAssigned()
	{
		_internalId = Game.Random.Int( 0, 500 );
	}
}

using Editor;
using Sandbox;

namespace TTT;

[Category( "Weapons" )]
[EditorModel( "models/weapons/w_bekas.vmdl" )]
[HammerEntity]
[Library( "ttt_weapon_bekas" )]
[Title( "Bekas-M" )]
public partial class Bekas : Weapon
{
	private bool _attackedDuringReload = false;

	public override void ActiveStart( Player player )
	{
		base.ActiveStart( player );

		_attackedDuringReload = false;
		TimeSinceReload = 0f;
	}

	protected override bool CanReload()
	{
		if ( !base.CanReload() )
			return false;

		var rate = Info.PrimaryRate;
		if ( rate <= 0 )
			return true;

		return TimeSincePrimaryAttack > (1 / rate);
	}

	public override void Simulate( IClient owner )
	{
		base.Simulate( owner );

		if ( IsReloading && Input.Pressed( InputButton.PrimaryAttack ) )
			_attackedDuringReload = true;
	}

	protected override void OnReloadFinish()
	{
		IsReloading = false;

		TimeSincePrimaryAttack = 0;

		AmmoClip += TakeAmmo( 1 );

		if ( !_attackedDuringReload && AmmoClip < Info.ClipSize && Owner.AmmoCount( Info.AmmoType ) > 0 )
			Reload();
		else
			FinishReload();

		_attackedDuringReload = false;
	}

	[ClientRpc]
	protected void FinishReload()
	{
		ViewModelEntity?.SetAnimParameter( "reload_finished", true );
	}
}

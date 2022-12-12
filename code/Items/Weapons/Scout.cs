using Editor;
using Sandbox;

namespace TTT;

[Category( "Weapons" )]
[ClassName( "ttt_weapon_scout" )]
[EditorModel( "models/weapons/w_spr.vmdl" )]
[HammerEntity]
[Title( "Scout" )]
public class Scout : Weapon
{
	public bool IsScoped { get; private set; }

	private float _defaultFOV;
	private UI.Scope _sniperScopePanel;

	public override void ActiveStart( Player player )
	{
		base.ActiveStart( player );

		IsScoped = false;
		_defaultFOV = Sandbox.Camera.FieldOfView;
	}

	public override void Simulate( IClient client )
	{
		if ( Game.IsClient && Input.Pressed( InputButton.SecondaryAttack ) )
		{
			if ( Prediction.FirstTime )
			{
				SetScoped( !IsScoped );
				PlaySound( Strings.ScopeInSound );
			}
		}

		base.Simulate( client );
	}

	public override void BuildInput()
	{
		base.BuildInput();

		if ( IsScoped )
			Owner.ViewAngles = Angles.Lerp( Owner.OriginalViewAngles, Owner.ViewAngles, 0.1f );
	}

	protected override void CreateHudElements()
	{
		base.CreateHudElements();

		_sniperScopePanel = new UI.Scope( "/ui/scout-scope.png" ) { Parent = Game.RootPanel };
	}

	protected override void DestroyHudElements()
	{
		base.DestroyHudElements();

		Sandbox.Camera.FieldOfView = _defaultFOV;
		_sniperScopePanel.Delete( true );
	}

	private void SetScoped( bool isScoped )
	{
		IsScoped = isScoped;

		if ( IsScoped )
			_sniperScopePanel.Show();
		else
			_sniperScopePanel.Hide();

		ViewModelEntity.EnableDrawing = !IsScoped;
		HandsModelEntity.EnableDrawing = !IsScoped;

		Sandbox.Camera.FieldOfView = isScoped ? 10f : _defaultFOV;
	}
}

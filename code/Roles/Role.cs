using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TTT;

public abstract class Role : IEquatable<Role>, IEquatable<string>
{
	private static readonly Dictionary<Type, HashSet<Player>> _players = new();
	public RoleInfo Info { get; private set; }
	public Team Team => Info.Team;
	public Color Color => Info.Color;
	public HashSet<ItemInfo> ShopItems => Info.ShopItems;
	public bool CanRetrieveCredits => Info.CanRetrieveCredits;
	public bool CanTeamChat => Info.CanTeamChat;
	public bool CanAttachCorpses => Info.CanAttachCorpses;
	public string Title => Info.Title;
	public RoleInfo.KarmaConfig Karma => Info.Karma;
	public RoleInfo.ScoringConfig Scoring => Info.Scoring;

	public Role()
	{
		Info = GameResource.GetInfo<RoleInfo>( GetType() );
	}

	public virtual void OnSelect( Player player )
	{
		if ( player.IsLocalPawn )
		{
			Player.RoleButtons = GetRoleButtons();

			foreach ( var roleButton in Player.RoleButtons )
				Player.RoleButtonMarkers.Add( new UI.RoleButtonMarker( roleButton ) );

			return;
		}
		else if ( !Game.IsServer )
		{
			if ( ShouldCreateRolePlate( player ) )
				player.Components.Create<UI.RolePlate>();

			return;
		}

		player.Credits = Math.Max( Info.DefaultCredits, player.Credits );
		player.PurchasedLimitedShopItems.Clear();
	}

	public virtual void OnDeselect( Player player )
	{
		if ( player.IsLocalPawn )
			player.ClearButtons();
		else if ( !Game.IsServer )
			player.Components.RemoveAny<UI.RolePlate>();
	}

	protected virtual bool ShouldCreateRolePlate( Player player )
	{
		return false;
	}

	protected List<RoleButton> GetRoleButtons()
	{
		Game.AssertClient();

		return Entity.All
				.OfType<RoleButton>()
				.Where( x => x.IsValid() && (x.RoleName == "All" || this == x.RoleName) )
				.ToList();
	}

	public static IEnumerable<Player> GetPlayers<T>() where T : Role
	{
		var type = typeof( T );

		if ( !_players.ContainsKey( type ) )
			_players.Add( type, new HashSet<Player>() );

		return _players[type];
	}

	[GameEvent.Player.RoleChanged]
	private static void OnPlayerRoleChanged( Player player, Role oldRole )
	{
		if ( oldRole is not null )
			_players[oldRole.GetType()].Remove( player );

		var newRole = player.Role;
		if ( newRole is not null )
		{
			if ( !_players.ContainsKey( newRole.GetType() ) )
				_players.Add( newRole.GetType(), new HashSet<Player>() );

			_players[newRole.GetType()].Add( player );
		}
	}

	public static bool operator ==( Role left, Role right )
	{
		if ( left is null )
		{
			if ( right is null )
				return true;

			return false;
		}

		return left.Equals( right );
	}
	public static bool operator !=( Role left, Role right ) => !(left == right);

	public static bool operator ==( Role left, string right )
	{
		if ( left is null || right is null )
			return false;

		return left.Equals( right );
	}
	public static bool operator !=( Role left, string right ) => !(left == right);

	public bool Equals( Role other )
	{
		if ( other is null )
			return false;

		if ( ReferenceEquals( this, other ) )
			return true;

		return Info == other.Info;
	}

	public bool Equals( string other )
	{
		if ( Info.Title.Equals( other, StringComparison.OrdinalIgnoreCase ) )
			return true;

		if ( Info.ClassName.Equals( other, StringComparison.OrdinalIgnoreCase ) )
			return true;

		return false;
	}

	public override bool Equals( object obj ) => Equals( obj as Role );

	public override int GetHashCode() => Info.ResourceId.GetHashCode();

#if SANDBOX && DEBUG
	[Event.Hotload]
	private void OnHotload()
	{
		Info = GameResource.GetInfo<RoleInfo>( GetType() );
		Player.RoleButtons = GetRoleButtons();
	}
#endif
}

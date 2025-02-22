using Sandbox;
using System.Collections;
using System.Collections.Generic;

namespace TTT;

/// <summary>
/// A subset of <see cref="Entity.Children"/> that contains entities 
/// of type <see cref="Carriable"/>.
/// </summary>
public sealed class Inventory : IEnumerable<Carriable>
{
	public Player Owner { get; private init; }

	public Carriable Active
	{
		get => Owner.ActiveCarriable;
		private set => Owner.ActiveCarriable = value;
	}

	public int Count => _list.Count;
	private readonly List<Carriable> _list = new();

	private readonly Dictionary<SlotType, int> _slotCapacity = new()
	{
		{ SlotType.Primary, 1 },
		{ SlotType.Secondary, 1 },
		{ SlotType.Melee, 1 },
		{ SlotType.OffensiveEquipment, 1 },
		{ SlotType.UtilityEquipment, 3 },
		{ SlotType.Grenade, 1 }
	};

	private readonly Dictionary<AmmoType, bool> _hasAmmoType = new()
	{
		{ AmmoType.None, false },
		{ AmmoType.PistolSMG, false },
		{ AmmoType.Shotgun, false },
		{ AmmoType.Sniper, false },
		{ AmmoType.Magnum, false },
		{ AmmoType.Rifle, false },
	};

	public Inventory( Player player ) => Owner = player;

	public bool Add( Carriable carriable, bool makeActive = false )
	{
		Game.AssertServer();

		if ( !carriable.IsValid() )
			return false;

		if ( carriable.Owner is not null )
			return false;

		if ( !CanAdd( carriable ) )
			return false;

		carriable.SetParent( Owner, true );

		if ( makeActive )
			SetActive( carriable );

		return true;
	}

	public bool CanAdd( Carriable carriable )
	{
		if ( Game.IsClient )
			return carriable.Parent == Owner;

		if ( !HasFreeSlot( carriable.Info.Slot ) )
			return false;

		if ( !carriable.CanCarry( Owner ) )
			return false;

		return true;
	}

	public bool Contains( Carriable entity )
	{
		return _list.Contains( entity );
	}

	public void Pickup( Carriable carriable )
	{
		if ( Add( carriable ) )
			Sound.FromEntity( "pickup_weapon", Owner );
	}

	public bool HasFreeSlot( SlotType slotType )
	{
		return _slotCapacity[slotType] > 0;
	}

	public bool HasWeaponOfAmmoType( AmmoType ammoType )
	{
		return ammoType != AmmoType.None && _hasAmmoType[ammoType];
	}

	public void OnUse( Carriable carriable )
	{
		Game.AssertServer();

		if ( !carriable.CanCarry( Owner ) )
			return;

		if ( HasFreeSlot( carriable.Info.Slot ) )
		{
			Add( carriable );
			return;
		}

		var entities = _list.FindAll( x => x.Info.Slot == carriable.Info.Slot );

		if ( Active is not null && Active.Info.Slot == carriable.Info.Slot )
		{
			if ( DropActive() is not null )
				Add( carriable, true );
		}
		else if ( entities.Count == 1 )
		{
			if ( Drop( entities[0] ) )
				Add( carriable, false );
		}
	}

	public bool SetActive( Carriable carriable )
	{
		if ( Active == carriable )
			return false;

		if ( !Contains( carriable ) )
			return false;

		Active = carriable;

		return true;
	}

	public T Find<T>() where T : Carriable
	{
		foreach ( var carriable in _list )
		{
			if ( carriable is not T t || t.Equals( default( T ) ) )
				continue;

			return t;
		}

		return null;
	}

	public bool Drop( Carriable carriable )
	{
		if ( !Game.IsServer )
			return false;

		if ( !Contains( carriable ) )
			return false;

		if ( !carriable.Info.CanDrop )
			return false;

		carriable.Parent = null;

		return true;
	}

	public Carriable DropActive()
	{
		if ( !Game.IsServer )
			return null;

		if ( Drop( Active ) )
		{
			var active = Active;
			Active = null;
			return active;
		}

		return null;
	}

	public void DropAll()
	{
		Game.AssertServer();

		foreach ( var carriable in _list.ToArray() )
			Drop( carriable );

		Active = null;

		DeleteContents();
	}

	public void DeleteContents()
	{
		Game.AssertServer();

		foreach ( var carriable in _list.ToArray() )
			carriable.Delete();

		Active = null;

		_list.Clear();
	}

	public void OnChildAdded( Carriable carriable )
	{
		if ( !CanAdd( carriable ) )
			return;

		if ( _list.Contains( carriable ) )
			throw new System.Exception( "Trying to add to inventory multiple times. This is gated by Entity:OnChildAdded and should never happen!" );

		_list.Add( carriable );

		carriable.OnCarryStart( Owner );

		_slotCapacity[carriable.Info.Slot] -= 1;

		if ( carriable is Weapon weapon )
			_hasAmmoType[weapon.Info.AmmoType] = true;
	}

	public void OnChildRemoved( Carriable carriable )
	{
		if ( !_list.Remove( carriable ) )
			return;

		carriable.OnCarryDrop( Owner );

		_slotCapacity[carriable.Info.Slot] += 1;

		if ( carriable is Weapon weapon )
			_hasAmmoType[weapon.Info.AmmoType] = false;
	}

	public T DropEntity<T>( Deployable<T> self ) where T : ModelEntity, new()
	{
		Game.AssertServer();

		var carriable = self as Carriable;
		if ( !carriable.IsValid() || !Contains( carriable ) )
			return null;

		carriable.Parent = null;
		carriable.Delete();

		var droppedEntity = new T
		{
			Owner = Owner,
			Position = Owner.EyePosition,
			Rotation = Owner.EyeRotation,
			Velocity = Owner.GetDropVelocity( false ),
			PhysicsEnabled = true,
		};

		droppedEntity.Tags.Add( "interactable" );
		droppedEntity.Tags.Remove( "solid" );

		return droppedEntity;
	}

	public IEnumerator<Carriable> GetEnumerator() => _list.GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

using System.Linq;
using Sandbox.UI;
using Sandbox.UI.Construct;

namespace TTT.UI;

[UseTemplate]
public class EventSummary : Panel
{
	public static EventSummary Instance;

	private Panel Empty { get; init; }
	private Panel Header { get; init; }
	private Panel Events { get; init; }

	public EventSummary()
	{
		Instance = this;
		Init();
	}

	public void Init()
	{
		Events.DeleteChildren();

		if ( GeneralMenu.Instance is not null )
		{
			// We should remove this once we can just send everything down as one list.
			int eventCount = GeneralMenu.Instance.LastEventSummaryData.Events?.Length ?? 0;
			int eventDescriptionCount = GeneralMenu.Instance.LastEventSummaryData.EventDescriptions?.Length ?? 0;

			if ( eventCount == eventDescriptionCount )
			{
				for ( int i = 0; i < eventCount; ++i )
				{
					var eventInfo = GeneralMenu.Instance.LastEventSummaryData.Events[i];
					var eventDescription = GeneralMenu.Instance.LastEventSummaryData.EventDescriptions[i];
					AddEvent( eventInfo, eventDescription );
				}
			}
		}

		Empty.Enabled( !Events.Children.Any() );
		Header.Enabled( Events.Children.Any() );
	}

	private void AddEvent( EventInfo eventInfo, string description )
	{
		var container = Events.Add.Panel( "event" );
		container.Add.Label( GetIcon( eventInfo.EventType ), "icon" );
		container.Add.Label( eventInfo.Time.TimerString(), "time" );
		container.Add.Label( description, "desc" );
	}

	private string GetIcon( EventType eventType )
	{
		return eventType switch
		{
			EventType.Round => "flag",
			EventType.PlayerTookDamage => "error",
			EventType.PlayerKill => "group",
			EventType.PlayerSuicide => "person",
			EventType.PlayerCorpseFound => "search",
			_ => string.Empty,
		};
	}
}

using Sandbox;
using Sandbox.UI;

namespace TTT.UI;

[UseTemplate]
public class Hud : RootPanel
{
	public Hud()
	{
		Game.RootPanel = this;
	}
}

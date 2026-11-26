// Wheel of Luck for s&box - the paytable window (port of CreatePayTableUI, opened in GMod with !wheelofluck).
// Look at a machine and press Reload (R) to open or close it. Built in code like the DarkRP addon's panels.
// MIT License, Copyright (c) 2020 In Memory of <CODE BLUE>. See WHEELOFLUCK_NOTICE.txt.
using Sandbox.UI;

public sealed class WOLPaytableState
{
	public Scene Scene;
	public GameObject Root;
	public WheelOfLuck Machine;
	public bool Open;
	public int Version;
}

public static class WOLPaytableUi
{
	public static readonly WOLPaytableState H = new WOLPaytableState();

	/// <summary>Called every frame by each machine on clients: opens/closes the paytable for the machine in view.</summary>
	public static void Watch( WheelOfLuck m )
	{
		var scene = m.Scene;
		if ( scene == null || scene.IsEditor ) return;
		var cam = scene.Camera;
		if ( cam == null ) return;
		var centre = m.WorldPosition + m.WorldRotation * new Vector3( 20, 0, 60 );
		var to = centre - cam.WorldPosition;
		var looking = to.Length < 160 && cam.WorldRotation.Forward.Dot( to.Normal ) > 0.75f;
		if ( H.Open && H.Machine == m && (!looking || !m.IsValid()) ) { H.Open = false; H.Version++; }
		if ( looking && Input.Pressed( "Reload" ) )
		{
			Ensure( scene );
			H.Open = !(H.Open && H.Machine == m);
			H.Machine = m;
			H.Version++;
		}
	}

	public static void Ensure( Scene scene )
	{
		if ( H.Scene == scene && H.Root.IsValid() ) return;
		H.Scene = scene;
		H.Open = false;
		var go = new GameObject( true, "wheelofluck_ui" );
		go.Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		H.Root = go;
		var sp = go.AddComponent<ScreenPanel>();
		sp.ZIndex = 105;
		go.Components.Create( TypeLibrary.GetType( "WOLPaytablePanel" ) );
	}
}

public sealed class WOLPaytablePanel : PanelComponent
{
	const string Css = @"
.wol { position: absolute; left: 0; right: 0; top: 0; bottom: 0; justify-content: center; align-items: center; pointer-events: none; opacity: 0; }
.wol.open { opacity: 1; }
.wol .sheet { width: 700px; height: 500px; background-image: url( materials/wheelofluck/ui/paytable.png ); background-size: 100% 100%; position: relative; }
.wol .v { position: absolute; font-family: Roboto; font-size: 25px; font-weight: 900; color: #282828; height: 30px; margin-top: -15px; align-items: center; }
.wol .hint { position: absolute; bottom: -28px; left: 0; right: 0; justify-content: center; font-family: Roboto; font-size: 14px; color: #fff; text-shadow: 0 1px 3px #000; }
";

	Panel _wrap;
	readonly List<Label> _values = new List<Label>();
	int _version = -1;
	string _cfgKey = "";

	protected override void OnUpdate()
	{
		var root = Panel;
		if ( root == null ) return;
		if ( _wrap == null || !_wrap.IsValid() ) Build( root );
		var h = WOLPaytableUi.H;
		var m = h.Machine;
		var key = m.IsValid() ? m.ConfigJson : "";
		if ( h.Version == _version && key == _cfgKey ) return;
		_version = h.Version;
		_cfgKey = key;
		_wrap.SetClass( "open", h.Open && m.IsValid() );
		if ( !m.IsValid() ) return;
		var cfg = WOLConfig.FromJson( m.ConfigJson );
		var amounts = new long[] { cfg.ThreeBonus, cfg.TwoBonus, cfg.OneBonus, cfg.ThreeRaspberry, cfg.ThreeCoins, cfg.TwoCoins,
			cfg.ThreeBar2, cfg.ThreeBar, cfg.ThreeSeven, cfg.ThreeDiamonds, cfg.TwoDiamonds, cfg.OneDiamonds, cfg.PricePerSpin };
		for ( int i = 0; i < _values.Count && i < amounts.Length; i++ ) _values[i].Text = WOLPaytable.Money( cfg, amounts[i] );
	}

	void Value( Panel sheet, float x, float y )
	{
		var l = sheet.AddChild<Label>();
		l.AddClass( "v" );
		l.Style.Left = Length.Pixels( x );
		l.Style.Top = Length.Pixels( y );
		_values.Add( l );
	}

	void Build( Panel root )
	{
		root.StyleSheet.Parse( Css );
		_values.Clear();
		_wrap = root.AddChild<Panel>();
		_wrap.AddClass( "wol" );
		var sheet = _wrap.AddChild<Panel>();
		sheet.AddClass( "sheet" );
		// Same positions as the GMod paytable (left column x 220, right column x 585, y is the text centre).
		Value( sheet, 220, 200 );
		Value( sheet, 220, 238 );
		Value( sheet, 220, 284 );
		Value( sheet, 220, 334 );
		Value( sheet, 220, 384 );
		Value( sheet, 220, 434 );
		Value( sheet, 585, 200 );
		Value( sheet, 585, 238 );
		Value( sheet, 585, 278 );
		Value( sheet, 585, 340 );
		Value( sheet, 585, 386 );
		Value( sheet, 585, 430 );
		Value( sheet, 212, 482 );
		var hint = sheet.AddChild<Label>();
		hint.AddClass( "hint" );
		hint.Text = "Press R again or look away to close";
		_version = -1;
	}
}

// Wheel of Luck for s&box - the slot machine entity.
// Ported from Blue's Slots "Wheel of Luck" for Garry's Mod (entities/wheel_of_luck, entities/wol_reel),
// MIT License, Copyright (c) 2020 In Memory of <CODE BLUE>. Model by Zerochain. See WHEELOFLUCK_NOTICE.txt.
//
// Networking: the host decides everything (prices, reel results, payouts, jackpot) and pays through DarkRP.
// Clients get the machine's state through [Sync] properties and the animations through host-only broadcasts;
// every client builds the moving parts, text and lights locally (not networked).
//
// Model parts (models/wheelofluck, positions in the machine's space; +X is the front):
//   body (root, has the physics), ring, bulbs, lever (pivot 13.46 22.95 36.29), button (32.75 0 52.21),
//   bonuswheel (18.81 0 114.41), three reels (18.81 -11.25/-3.94/3.37 62.66).

public sealed class WOLState
{
	/// <summary>Host: connection id -> last paid spin time.</summary>
	public readonly Dictionary<Guid, float> LastSpin = new Dictionary<Guid, float>();
	/// <summary>Host: connection id -> machine (GameObject id) of that spin.</summary>
	public readonly Dictionary<Guid, Guid> LastMachine = new Dictionary<Guid, Guid>();
	public Scene Scene;
}

public static class WOLGlobal
{
	public static readonly WOLState H = new WOLState();

	/// <summary>Static state survives play sessions; start over when the scene changes.</summary>
	public static WOLState For( Scene scene )
	{
		if ( H.Scene != scene ) Reset( scene );
		return H;
	}

	public static void Reset( Scene scene )
	{
		WOLMoney.Reset();
		H.LastSpin.Clear();
		H.LastMachine.Clear();
		H.Scene = scene;
	}
}

[Title( "Wheel of Luck" ), Category( "Blue's Slots" ), Icon( "casino" )]
public sealed class WheelOfLuck : Component, Component.IPressable
{
	// ======== synced state (host writes) ========
	[Sync( SyncFlags.FromHost )] public long Jackpot { get; set; }
	[Sync( SyncFlags.FromHost )] public int BonusSpins { get; set; }
	[Sync( SyncFlags.FromHost )] public long LastWin { get; set; }
	/// <summary>Light state: 0 normal, 1 bonus, 2 jackpot, 3 all off (GMod skins 0/1/2/4).</summary>
	[Sync( SyncFlags.FromHost )] public int Lights { get; set; }
	[Sync( SyncFlags.FromHost )] public bool Busy { get; set; }
	/// <summary>The host's config (JSON), for the paytable and wheel labels.</summary>
	[Sync( SyncFlags.FromHost )] public string ConfigJson { get; set; } = "";
	/// <summary>Where the reels stopped ("1,2,3"), so late joiners see them.</summary>
	[Sync( SyncFlags.FromHost )] public string ReelItems { get; set; } = "";
	/// <summary>Bonus wheel angle after the last bonus spin.</summary>
	[Sync( SyncFlags.FromHost )] public float WheelAngle { get; set; }

	// ======== host only ========
	WOLConfig _cfg;
	Connection _spinner;
	public bool HostStarted;

	public WOLConfig Config()
	{
		if ( _cfg == null ) _cfg = IsProxy ? WOLConfig.FromJson( ConfigJson ) : WOLConfig.LoadHost();
		return _cfg;
	}

	protected override void OnStart()
	{
		if ( !IsProxy && !HostStarted )
		{
			HostStarted = true;
			_cfg = WOLConfig.LoadHost();
			ConfigJson = _cfg.ToJson( false );
			if ( Jackpot <= 0 ) Jackpot = RandomJackpot();
		}
		if ( !IsProxy ) { WOLGlobal.For( Scene ); WOLMoney.EnsureBridge( GameObject ); }
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy ) return;
		WOLMoney.EnsureBridge( GameObject );
		RunSteps();
	}

	long RandomJackpot()
	{
		var c = Config();
		return (long)(c.JackpotResetMin + Game.Random.Float( 0f, 1f ) * (c.JackpotResetMax - c.JackpotResetMin));
	}

	// ======== pressing E ========

	public bool CanPress( Component.IPressable.Event e ) => true;
	public bool Pressing( Component.IPressable.Event e ) => false;
	public void Release( Component.IPressable.Event e ) { }

	public bool Press( Component.IPressable.Event e )
	{
		RequestUse();
		return true;
	}

	public Component.IPressable.Tooltip? GetTooltip( Component.IPressable.Event e )
	{
		var c = Config();
		if ( Busy ) return new Component.IPressable.Tooltip( "Spinning...", "casino", null );
		if ( BonusSpins > 0 ) return new Component.IPressable.Tooltip( "Bonus spin (" + BonusSpins + " left)", "casino", "Free" );
		return new Component.IPressable.Tooltip( "Spin " + WOLPaytable.Money( c, c.PricePerSpin ), "casino", "R: paytable" );
	}

	[Rpc.Host]
	public void RequestUse()
	{
		var caller = Rpc.Caller;
		if ( caller == null ) caller = Connection.Local;
		Use( caller );
	}

	/// <summary>Host: someone used the machine. Port of ENT:Use with the anti-theft rules.</summary>
	public void Use( Connection caller )
	{
		if ( IsProxy || Busy || caller == null ) return;
		var st = WOLGlobal.For( Scene );
		var cfg = Config();
		var now = Time.Now;
		// Someone else is still on this machine.
		if ( _spinner != null && _spinner != caller && Connection.All.Contains( _spinner ) )
		{
			var sid = _spinner.Id;
			if ( st.LastSpin.ContainsKey( sid ) && now - st.LastSpin[sid] < cfg.AntiTheftTime
				&& st.LastMachine.ContainsKey( sid ) && st.LastMachine[sid] == GameObject.Id )
			{
				Tell( caller, 1, "Please wait for " + _spinner.DisplayName + " to finish their turn or find a new machine." );
				return;
			}
		}
		// The caller recently played another machine.
		var cid = caller.Id;
		float last = st.LastSpin.ContainsKey( cid ) ? st.LastSpin[cid] : -99999f;
		if ( st.LastMachine.ContainsKey( cid ) && st.LastMachine[cid] != GameObject.Id && now - last < cfg.AntiTheftTime )
		{
			Tell( caller, 1, "Please wait " + (int)Math.Ceiling( cfg.AntiTheftTime - (now - last) ) + " seconds before switching machines." );
			return;
		}
		_spinner = caller;
		if ( BonusSpins > 0 ) BonusSpin();
		else Spin();
	}

	void Tell( Connection c, int type, string text )
	{
		if ( c == null ) return;
		using ( Rpc.FilterInclude( c ) ) NoticeRpc( type, text );
	}

	// ======== host: timeline ========
	// The GMod version chains timer.Simple calls. Here the host keeps a list of timed steps and runs them in
	// OnFixedUpdate (script async continuations don't resume reliably).

	const int OpLever = 1, OpReelsStart = 2, OpStopReel = 3, OpBuildup = 4, OpSpinEnd = 5, OpBonusLights = 6,
		OpWheelDone = 10, OpBonusPay = 11, OpJackpotZero = 13, OpJackpotEnd = 14;
	List<float> _stepAt;
	List<int> _stepOp, _stepA;
	int _item1, _item2, _item3, _bonusItem;

	void Schedule( float delay, int op, int a )
	{
		if ( _stepAt == null ) { _stepAt = new List<float>(); _stepOp = new List<int>(); _stepA = new List<int>(); }
		_stepAt.Add( Time.Now + delay );
		_stepOp.Add( op );
		_stepA.Add( a );
	}

	void RunSteps()
	{
		if ( _stepAt == null ) return;
		while ( true )
		{
			int next = -1;
			for ( int i = 0; i < _stepAt.Count; i++ )
				if ( _stepAt[i] <= Time.Now && (next < 0 || _stepAt[i] < _stepAt[next]) ) next = i;
			if ( next < 0 ) return;
			var op = _stepOp[next];
			var a = _stepA[next];
			_stepAt.RemoveAt( next );
			_stepOp.RemoveAt( next );
			_stepA.RemoveAt( next );
			RunStep( op, a );
		}
	}

	void RunStep( int op, int a )
	{
		var cfg = Config();
		if ( op == OpLever ) { LeverRpc(); SoundRpc( "sounds/wheelofluck/lever_pulled.sound" ); }
		else if ( op == OpReelsStart ) { LastWin = 0; ReelsStartRpc(); }
		else if ( op == OpStopReel ) StopReelRpc( a, a == 0 ? _item1 : (a == 1 ? _item2 : _item3) );
		else if ( op == OpBuildup ) SoundRpc( "sounds/wheelofluck/buildup.sound" );
		else if ( op == OpSpinEnd ) SpinEnd();
		else if ( op == OpBonusLights ) { if ( BonusSpins > 0 && (Lights == 0 || a == 1) ) Lights = 1; }
		else if ( op == OpWheelDone ) WheelDone();
		else if ( op == OpBonusPay ) BonusPay();
		else if ( op == OpJackpotZero ) { Lights = 2; Jackpot = 0; }
		else if ( op == OpJackpotEnd ) JackpotEnd();
	}

	// ======== host: a paid spin ========

	void Spin()
	{
		var cfg = Config();
		var c = _spinner;
		if ( !WOLMoney.Available( c ) )
		{
			Tell( c, 1, "The Wheel of Luck takes DarkRP money, and DarkRP isn't running." );
			return;
		}
		if ( !WOLMoney.CanAfford( c, cfg.PricePerSpin ) )
		{
			Tell( c, 1, "You cannot afford to use this machine, you need at least " + WOLPaytable.Money( cfg, cfg.PricePerSpin ) + "." );
			return;
		}
		WOLMoney.Add( c, -cfg.PricePerSpin );
		var st = WOLGlobal.For( Scene );
		st.LastSpin[c.Id] = Time.Now;
		st.LastMachine[c.Id] = GameObject.Id;
		Jackpot += cfg.JackpotIncreasePerSpin;

		_item1 = WOLPaytable.RandomItem( cfg );
		_item2 = WOLPaytable.RandomItem( cfg );
		_item3 = WOLPaytable.RandomItem( cfg );
		if ( _forceReels != null ) { _item1 = _forceReels[0]; _item2 = _forceReels[1]; _item3 = _forceReels[2]; }
		float extraWait = 0;
		// Two the same: wait longer for suspense, and sometimes the third matches too.
		if ( _item1 == _item2 )
		{
			extraWait = 2;
			if ( _forceReels == null && Game.Random.Int( 0, 100 ) < cfg.ThirdReelMatchChance ) _item3 = _item2;
		}
		_forceReels = null;
		Busy = true;
		Schedule( 0, OpLever, 0 );
		Schedule( 0.5f, OpReelsStart, 0 );
		Schedule( 2.5f, OpStopReel, 0 );
		Schedule( 3.5f, OpStopReel, 1 );
		if ( extraWait > 0 ) Schedule( 3.5f, OpBuildup, 0 );
		Schedule( 4.5f + extraWait, OpStopReel, 2 );
		Schedule( 4.5f + extraWait, OpSpinEnd, 0 );
	}

	void SpinEnd()
	{
		var cfg = Config();
		ReelItems = _item1 + "," + _item2 + "," + _item3;
		var reward = WOLPaytable.Evaluate( cfg, _item1, _item2, _item3 );
		if ( reward.Spins > 0 ) BonusSpins += reward.Spins;
		if ( reward.Spins > 0 || reward.Cash > 0 ) FlashRpc( reward.R1, reward.R2, reward.R3 );
		LastWin = reward.Cash;
		Pay( _spinner, reward.Cash );
		Busy = false;
		if ( reward.Spins <= 0 )
		{
			SoundRpc( reward.Sound ?? "sounds/wheelofluck/reel_stop_03.sound" );
			return;
		}
		BonusMusicRpc( true );
		Schedule( 1.66f, OpBonusLights, 0 );
	}

	// ======== host: a bonus wheel spin ========

	void BonusSpin()
	{
		_bonusItem = _forceWedge > 0 ? _forceWedge : Game.Random.Int( 1, 20 );
		_forceWedge = 0;
		Busy = true;
		BonusSpins -= 1;
		var start = WheelAngle % 360;
		var target = (360 - 18 * (_bonusItem - 1)) + 360 * 5;
		BonusWheelRpc( start, target );
		WheelAngle = target % 360;
		Schedule( 10, OpWheelDone, 0 );
	}

	void WheelDone()
	{
		var cfg = Config();
		if ( _bonusItem != 1 ) { Schedule( 2, OpBonusPay, 0 ); return; }
		// JACKPOT
		var c = _spinner;
		var won = Jackpot;
		LastWin = won;
		Pay( c, won );
		EndBonus();
		Lights = 3;
		var name = c != null ? c.DisplayName : "Someone";
		var everyone = Connection.All.ToList();
		if ( everyone.Count == 0 && Connection.Local != null ) everyone.Add( Connection.Local );
		foreach ( var other in everyone ) Tell( other, 4, "HOLY SMOKES! " + name + " JUST WON A CRAZY JACKPOT OF " + WOLPaytable.Money( cfg, won ) + " ON WHEEL OF LUCK!" );
		JackpotRpc( true, won );
		Schedule( 2.28f, OpJackpotZero, 0 );
		Schedule( 45, OpJackpotEnd, 0 );
	}

	void BonusPay()
	{
		var cfg = Config();
		var cash = cfg.BonusWheel[Math.Clamp( _bonusItem - 2, 0, cfg.BonusWheel.Count - 1 )];
		LastWin = cash;
		Pay( _spinner, cash );
		if ( BonusSpins < 1 ) EndBonus();
		Busy = false;
	}

	void JackpotEnd()
	{
		JackpotRpc( false, 0 );
		Jackpot = RandomJackpot();
		Busy = false;
		if ( BonusSpins > 0 )
		{
			BonusMusicRpc( true );
			Schedule( 1.66f, OpBonusLights, 1 );
		}
		else Lights = 0;
	}

	void EndBonus()
	{
		BonusMusicRpc( false );
		Lights = 0;
	}

	void Pay( Connection c, long amount )
	{
		if ( amount <= 0 ) return;
		MoneyBurstRpc( amount );
		if ( c == null ) return;
		WOLMoney.Add( c, amount );
		Tell( c, 4, "Congratulations! You just won " + WOLPaytable.Money( Config(), amount ) );
	}

	// ======== host -> clients ========

	[Rpc.Broadcast( NetFlags.HostOnly )]
	public void NoticeRpc( int type, string text )
	{
		var color = type == 1 ? new Color( 0.95f, 0.3f, 0.3f ) : new Color( 0.35f, 0.85f, 0.4f );
		try { Sandbox.UI.Notices.AddNotice( type == 1 ? "error" : "casino", color, text, 5 ); }
		catch { Log.Info( text ); }
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	public void SoundRpc( string sound ) { PlaySound( sound, WorldPosition + WorldRotation * new Vector3( 25, 0, 60 ) ); }

	[Rpc.Broadcast( NetFlags.HostOnly )]
	public void LeverRpc() { _leverStart = Time.Now; }

	[Rpc.Broadcast( NetFlags.HostOnly )]
	public void ReelsStartRpc()
	{
		EnsureParts();
		for ( int i = 0; i < 3; i++ ) { _reelSpinning[i] = true; SetReelSkin( i, 2 ); }
		_motor = PlaySound( "sounds/wheelofluck/reel_rotating.sound", WorldPosition + WorldRotation * new Vector3( 25, 0, 62 ) );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	public void StopReelRpc( int reel, int item )
	{
		EnsureParts();
		if ( reel < 0 || reel > 2 ) return;
		var target = ReelAngle( item );
		_reelTarget[reel] = target;
		_reelAngle[reel] = target + 30;
		_reelSpinning[reel] = false;
		SetReelSkin( reel, 1 );
		PlaySound( "sounds/wheelofluck/reel_stop_0" + (reel + 1) + ".sound", WorldPosition + WorldRotation * new Vector3( 25, 0, 62 ) );
		if ( reel == 2 && _motor != null ) { _motor.Stop( 0.1f ); _motor = null; }
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	public void FlashRpc( bool r1, bool r2, bool r3 )
	{
		InitClientState();
		_flashStart = Time.Now;
		_flash[0] = r1; _flash[1] = r2; _flash[2] = r3;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	public void BonusWheelRpc( float from, float to )
	{
		_wheelFrom = from;
		_wheelTo = to;
		_wheelStart = Time.Now;
		_wheelSpinning = true;
		_buttonStart = Time.Now;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	public void BonusMusicRpc( bool on )
	{
		StopMusic();
		_partyMode = 0;
		if ( !on ) return;
		_music = PlaySound( "sounds/wheelofluck/bonus_sound.sound", WorldPosition + WorldRotation * new Vector3( 20, 0, 90 ) );
		_partyMode = 1;
		_partyStart = Time.Now + 1.66f;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	public void JackpotRpc( bool on, long amount )
	{
		StopMusic();
		_partyMode = 0;
		_jackpotCounting = false;
		if ( !on ) return;
		_music = PlaySound( "sounds/wheelofluck/jackpot_sound.sound", WorldPosition + WorldRotation * new Vector3( 20, 0, 90 ) );
		_jackpotFrom = amount;
		_jackpotStart = Time.Now + 2.28f;
		_jackpotCounting = true;
		_partyMode = 2;
		_partyStart = Time.Now + 2.28f;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	public void MoneyBurstRpc( long amount )
	{
		int bursts = 1;
		if ( amount >= 1500 ) bursts += 1;
		if ( amount >= 10000 ) bursts += 2;
		if ( amount >= 100000 ) bursts += 3;
		if ( amount >= 200000 ) bursts += 5;
		MoneyBurst( bursts );
	}

	// ======== client presentation ========

	// The reel art is centred on the octagon's edges: symbol 1 (bonus) sits at 4.5 degrees above +X at rest, the window
	// faces 7.2 degrees up, and each symbol is 45 degrees further round.
	static float ReelAngle( int item ) => -2.7f - 45f * (Math.Clamp( item, 1, 8 ) - 1);

	GameObject _parts;
	ModelRenderer _ring, _bulbs, _button;
	GameObject _lever, _wheel, _buttonGo, _lightGo;
	PointLight _light;
	List<ModelRenderer> _reels;
	float[] _reelAngle;
	float[] _reelTarget;
	bool[] _reelSpinning;
	int[] _reelSkin;
	bool[] _flash;
	float _flashStart;
	float _leverStart;
	float _buttonStart;
	bool _wheelSpinning;
	float _wheelFrom, _wheelTo, _wheelStart, _wheelShown;
	int _lastFace;
	float _lastClick;
	SoundHandle _motor, _music;
	/// <summary>0 none, 1 bonus party (reels flash, coloured light), 2 jackpot.</summary>
	int _partyMode;
	float _partyStart;
	bool _jackpotCounting;
	long _jackpotFrom;
	float _jackpotStart;
	TextRenderer _jackpotText, _lastWinText, _spinsText;
	List<TextRenderer> _wheelLabels;
	string _labelsKey;
	string _reelItemsShown;
	List<GameObject> _bursts;
	List<float> _burstTimes;

	/// <summary>Client state set up here instead of field initializers (the script interpreter rejects some of those).</summary>
	void InitClientState()
	{
		if ( _reelAngle != null ) return;
		_reels = new List<ModelRenderer>();
		_reelAngle = new float[3];
		_reelTarget = new float[3];
		_reelSpinning = new bool[3];
		_reelSkin = new int[3];
		_flash = new bool[3];
		_flashStart = -100;
		_leverStart = -100;
		_buttonStart = -100;
		_lastFace = -1;
		_wheelLabels = new List<TextRenderer>();
		_labelsKey = "";
		_reelItemsShown = "";
		_bursts = new List<GameObject>();
		_burstTimes = new List<float>();
	}

	static bool Dedicated() => Connection.Local != null && Connection.Local.IsDedicatedServer;

	SoundHandle PlaySound( string sound, Vector3 pos )
	{
		if ( string.IsNullOrEmpty( sound ) || Dedicated() ) return null;
		return Sound.Play( sound, pos );
	}

	void StopMusic()
	{
		if ( _music != null ) { _music.Stop( 0.3f ); _music = null; }
	}

	protected override void OnDestroy()
	{
		StopMusic();
		if ( _motor != null ) { _motor.Stop( 0 ); _motor = null; }
		if ( _parts.IsValid() ) _parts.Destroy();
	}

	GameObject Part( string name, GameObject parent, Vector3 pos )
	{
		var go = new GameObject( true, name );
		go.Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		go.SetParent( parent, false );
		go.LocalPosition = pos;
		return go;
	}

	ModelRenderer AddModel( GameObject go, string model )
	{
		var r = go.AddComponent<ModelRenderer>();
		r.Model = Sandbox.Model.Load( "models/wheelofluck/" + model + ".vmdl" );
		return r;
	}

	TextRenderer Text( string name, GameObject parent, Vector3 pos, Rotation rot, float height, Color color, string font, TextRenderer.HAlignment h )
	{
		var go = Part( name, parent, pos );
		go.LocalRotation = rot;
		var t = go.AddComponent<TextRenderer>();
		t.FontFamily = font;
		t.FontSize = 64;
		t.Scale = height / 64f;
		t.Color = color;
		t.HorizontalAlignment = h;
		t.VerticalAlignment = TextRenderer.VAlignment.Center;
		return t;
	}

	/// <summary>Builds the moving parts, screens and light on this machine (every client, not networked).</summary>
	void EnsureParts()
	{
		InitClientState();
		if ( _parts.IsValid() || Dedicated() ) return;
		_parts = Part( "wol_parts", GameObject, Vector3.Zero );
		_ring = AddModel( Part( "ring", _parts, Vector3.Zero ), "wheelofluck_ring" );
		_bulbs = AddModel( Part( "bulbs", _parts, Vector3.Zero ), "wheelofluck_bulbs" );
		_lever = Part( "lever", _parts, new Vector3( 13.465f, 22.945f, 36.288f ) );
		AddModel( _lever, "wheelofluck_lever" );
		_buttonGo = Part( "button", _parts, new Vector3( 32.749f, 0, 52.213f ) );
		_button = AddModel( _buttonGo, "wheelofluck_button" );
		_wheel = Part( "bonuswheel", _parts, new Vector3( 18.805f, 0, 114.411f ) );
		AddModel( _wheel, "wheelofluck_bonuswheel" );
		var reelY = new float[] { -11.251f, -3.941f, 3.369f };
		_reels.Clear();
		for ( int i = 0; i < 3; i++ )
		{
			var r = AddModel( Part( "reel" + (i + 1), _parts, new Vector3( 18.805f, reelY[i], 62.661f ) ), "wheelofluck_reel" );
			_reels.Add( r );
			_reelAngle[i] = ReelAngle( 1 + i * 2 );
			_reelTarget[i] = _reelAngle[i];
			_reelSkin[i] = -1;
			SetReelSkin( i, 1 );
		}

		// The LED screens (DSEG14 font as in GMod; "~" lights every segment, drawn dim behind the digits).
		var face = Rotation.From( 0, 180, 0 );
		var tilted = Rotation.From( 5, 180, 0 );
		var ghost = new Color( 0.12f, 0.12f, 0.12f, 0.6f );
		Text( "jackpot_ghost", _parts, new Vector3( 25.45f, 14.3f, 90.35f ), face, 2.5f, ghost, "DSEG14 Modern", TextRenderer.HAlignment.Right ).Text = "~~~~~~~~~~~~~~";
		_jackpotText = Text( "jackpot", _parts, new Vector3( 25.5f, 14.3f, 90.35f ), face, 2.5f, new Color( 1f, 0.843f, 0f ), "DSEG14 Modern", TextRenderer.HAlignment.Right );
		Text( "lastwin_ghost", _parts, new Vector3( 27.0f, 14.3f, 67.25f ), tilted, 0.7f, ghost, "DSEG14 Modern", TextRenderer.HAlignment.Right ).Text = "~~~~~~~~";
		_lastWinText = Text( "lastwin", _parts, new Vector3( 27.05f, 14.3f, 67.25f ), tilted, 0.7f, new Color( 0.86f, 0.86f, 0.86f ), "DSEG14 Modern", TextRenderer.HAlignment.Right );
		Text( "spins_ghost", _parts, new Vector3( 27.4f, 13.95f, 62.75f ), tilted, 0.7f, ghost, "DSEG14 Modern", TextRenderer.HAlignment.Right ).Text = "~~";
		_spinsText = Text( "spins", _parts, new Vector3( 27.45f, 13.95f, 62.75f ), tilted, 0.7f, new Color( 0.86f, 0.86f, 0.86f ), "DSEG14 Modern", TextRenderer.HAlignment.Right );

		_lightGo = Part( "party_light", _parts, new Vector3( 40, 0, 100 ) );
		_light = _lightGo.AddComponent<PointLight>();
		_light.Radius = 256;
		_light.Shadows = false;
		_light.Enabled = false;
	}

	void SetReelSkin( int i, int skin )
	{
		if ( i < 0 || i >= _reels.Count || _reelSkin[i] == skin ) return;
		_reelSkin[i] = skin;
		// GMod reel skins: 1 lit, 2 motion blur, anything else (3) the unlit texture.
		// (Material groups don't switch at runtime here, so swap the reel's only material directly.)
		_reels[i].MaterialOverride = Material.Load( "materials/wheelofluck/" + (skin == 1 ? "wheels_on" : (skin == 2 ? "wheels_blur" : "wheels_off")) + ".vmat" );
	}

	void UpdateWheelLabels()
	{
		var key = ConfigJson;
		if ( key == _labelsKey && _wheelLabels.Count > 0 ) return;
		_labelsKey = key;
		foreach ( var l in _wheelLabels ) if ( l.IsValid() ) l.GameObject.Destroy();
		_wheelLabels.Clear();
		var cfg = Config();
		if ( IsProxy ) cfg = WOLConfig.FromJson( ConfigJson );
		// Wedge i sits 18 degrees further round (from the top, towards the viewer's left) than wedge i-1.
		for ( int i = 2; i <= 20; i++ )
		{
			var phi = 90f + 18f * (i - 1);
			var rad = phi * MathF.PI / 180f;
			var pos = new Vector3( 3.1f, 11.8f * MathF.Cos( rad ), 11.8f * MathF.Sin( rad ) );
			var rot = Rotation.FromAxis( Vector3.Forward, phi ) * Rotation.FromYaw( 180 );
			var amount = cfg.BonusWheel[Math.Clamp( i - 2, 0, cfg.BonusWheel.Count - 1 )];
			var t = Text( "wedge" + i, _wheel, pos, rot, 1.5f, Color.Black, "Roboto", TextRenderer.HAlignment.Right );
			t.FontWeight = 900;
			t.Text = WOLPaytable.Money( cfg, amount );
			_wheelLabels.Add( t );
		}
	}

	static float Hermite( float t, float a, float b )
	{
		t = Math.Clamp( t, 0, 1 );
		return a + (b - a) * (t * t * (3f - 2f * t));
	}

	static Color Hsv( float h, float s, float v )
	{
		var i = (int)MathF.Floor( h * 6 );
		var f = h * 6 - i;
		var p = v * (1 - s);
		var q = v * (1 - f * s);
		var t = v * (1 - (1 - f) * s);
		i = ((i % 6) + 6) % 6;
		if ( i == 0 ) return new Color( v, t, p );
		if ( i == 1 ) return new Color( q, v, p );
		if ( i == 2 ) return new Color( p, v, t );
		if ( i == 3 ) return new Color( p, q, v );
		if ( i == 4 ) return new Color( t, p, v );
		return new Color( v, p, q );
	}

	protected override void OnUpdate()
	{
		if ( Dedicated() ) return;
		EnsureParts();
		if ( !_parts.IsValid() ) return;
		var now = Time.Now;
		var dt = Time.Delta;

		// Late joiners / first frame: show where the reels last stopped.
		if ( ReelItems != _reelItemsShown )
		{
			_reelItemsShown = ReelItems;
			var parts = (ReelItems ?? "").Split( ',' );
			if ( parts.Length == 3 && !_reelSpinning[0] )
				for ( int i = 0; i < 3; i++ ) { var it = (int)WOLConfig.ParseLong( parts[i], 0 ); if ( it > 0 ) _reelTarget[i] = ReelAngle( it ); }
		}

		// Reels: spin at 850 deg/s, then ease back from the 30 degree overshoot.
		for ( int i = 0; i < 3 && i < _reels.Count; i++ )
		{
			if ( _reelSpinning[i] ) _reelAngle[i] += 850 * dt;
			else
			{
				var diff = ((_reelTarget[i] - _reelAngle[i]) % 360 + 540) % 360 - 180;
				_reelAngle[i] += diff * Math.Min( 1, 10 * dt );
			}
			_reels[i].LocalRotation = Rotation.FromAxis( Vector3.Left, _reelAngle[i] );
		}

		// Lever: 0 -> 35 degrees in half a second and back.
		var lt = now - _leverStart;
		float lever = 0;
		if ( lt < 0.5f ) lever = Hermite( lt / 0.5f, 0, 35 );
		else if ( lt < 1f ) lever = Hermite( (lt - 0.5f) / 0.5f, 35, 0 );
		_lever.LocalRotation = Rotation.FromAxis( Vector3.Left, lever );

		// The big button goes in when a bonus spin starts ("press" sequence).
		var bt = now - _buttonStart;
		var press = bt < 0.15f ? bt / 0.15f : (bt < 0.6f ? 1 - (bt - 0.15f) / 0.45f : 0);
		_buttonGo.LocalPosition = new Vector3( 32.749f, 0, 52.213f ) + new Vector3( -0.97f, 0, 0.25f ) * (0.4f * press);

		// Bonus wheel: 10 seconds, eased, with a click every wedge.
		float wheel;
		if ( _wheelSpinning )
		{
			var wt = (now - _wheelStart) / 10f;
			wheel = Hermite( wt, _wheelFrom, _wheelTo );
			if ( wt >= 1 ) _wheelSpinning = false;
			var face = (int)MathF.Floor( ((wheel - 9) % 360 + 360) % 360 / 18 );
			if ( face != _lastFace )
			{
				_lastFace = face;
				if ( now - _lastClick >= 0.02f )
				{
					_lastClick = now;
					var h = PlaySound( "sounds/wheelofluck/bonus_click.sound", WorldPosition + WorldRotation * new Vector3( 20, 0, 114 ) );
					if ( h != null ) h.Pitch = Game.Random.Float( 1f, 1.08f );
				}
			}
		}
		else wheel = WheelAngle;
		_wheelShown = wheel;
		_wheel.LocalRotation = Rotation.FromAxis( Vector3.Forward, wheel );
		UpdateWheelLabels();

		// Lights (GMod skins): ring flashes in bonus/jackpot, bulbs blink when idle, button blinks in bonus.
		var mode = Lights;
		if ( mode == 1 || mode == 2 )
			_ring.MaterialOverride = Material.Load( "materials/wheelofluck/ringlight_flashy_" + ((int)(now * 24f / 9f) % 24).ToString( "00" ) + ".vmat" );
		else _ring.MaterialOverride = null;
		_bulbs.MaterialOverride = Material.Load( mode == 0 ? "materials/wheelofluck/genericlight_on_" + ((int)(now * 3) % 2) + ".vmat" : "materials/wheelofluck/genericlight_off.vmat" );
		_button.MaterialOverride = mode == 1 ? Material.Load( "materials/wheelofluck/mainbutton_blinking_" + ((int)(now * 4) % 2) + ".vmat" ) : null;

		// Reel lights: win flash, bonus/jackpot party alternation, or the normal state.
		var party = _partyMode != 0 && now >= _partyStart;
		for ( int i = 0; i < 3 && i < _reels.Count; i++ )
		{
			if ( _reelSpinning[i] ) { SetReelSkin( i, 2 ); continue; }
			int skin = 1;
			var ft = now - _flashStart;
			if ( ft < 0.36f && _flash[i] ) skin = ((int)(ft / 0.12f) % 2 == 0) ? 3 : 1;
			else if ( party )
			{
				var period = _partyMode == 2 ? 0.348f : 0.476f;
				var phase = (int)((now - _partyStart) / period) % 2;
				skin = ((i == 1) == (phase == 0)) ? 1 : 3;
			}
			SetReelSkin( i, skin );
		}
		_light.Enabled = party;
		if ( party ) { _light.LightColor = Hsv( ((now - _partyStart) * 0.1f) % 1f, 0.5f, 1f ) * 3f; }

		// Jackpot money every 0.696 s while the jackpot plays.
		if ( _partyMode == 2 && party )
		{
			var n = (int)((now - _partyStart) / 0.696f);
			if ( n != _lastJackpotBurst ) { _lastJackpotBurst = n; MoneyBurst( 1 ); }
		}

		// Screens.
		long jackpot = Jackpot;
		if ( _jackpotCounting && now >= _jackpotStart )
			jackpot = (long)MathX.Lerp( _jackpotFrom, 0, Math.Clamp( (now - _jackpotStart) / 42f, 0, 1 ) );
		else if ( _jackpotCounting ) jackpot = _jackpotFrom;
		_jackpotText.Text = WOLPaytable.Commas( jackpot );
		_lastWinText.Text = LastWin.ToString();
		_spinsText.Text = BonusSpins < 10 ? "0" + BonusSpins : BonusSpins.ToString();

		// Old money bursts.
		for ( int i = _bursts.Count - 1; i >= 0; i-- )
		{
			if ( now - _burstTimes[i] < 5 ) continue;
			if ( _bursts[i].IsValid() ) _bursts[i].Destroy();
			_bursts.RemoveAt( i );
			_burstTimes.RemoveAt( i );
		}

		WOLPaytableUi.Watch( this );
	}

	int _lastJackpotBurst;

	/// <summary>Dollar bills flying out of the machine (simple stand-in for GMod's wol_money_burst01 particle).</summary>
	void MoneyBurst( int count )
	{
		if ( Dedicated() ) return;
		var go = new GameObject( true, "wol_money" );
		go.Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		go.WorldPosition = WorldPosition + WorldRotation * new Vector3( 30, 0, 70 );
		var fx = go.AddComponent<ParticleEffect>();
		fx.MaxParticles = 200;
		fx.Lifetime = 3f;
		fx.Scale = 6f;
		fx.Force = true;
		fx.ForceDirection = new Vector3( 0, 0, -300 );
		var em = go.AddComponent<ParticleSphereEmitter>();
		em.Velocity = 150;
		em.Loop = false;
		em.Duration = 0.2f;
		em.Burst = 15 * Math.Clamp( count, 1, 12 );
		var r = go.AddComponent<ParticleSpriteRenderer>();
		r.Sprite = Sprite.FromTexture( Texture.Load( FileSystem.Mounted, "materials/wheelofluck/particles/money.png" ) );
		_bursts.Add( go );
		_burstTimes.Add( Time.Now );
	}

	// ======== test helpers (run_csharp can reach component instance methods) ========

	/// <summary>Editor/test: evaluate three reel items with this machine's config.</summary>
	public string TestEvaluate( int a, int b, int c )
	{
		var r = WOLPaytable.Evaluate( Config(), a, b, c );
		return r.Cash + "|" + r.Spins + "|" + r.R1 + r.R2 + r.R3;
	}

	/// <summary>Editor/test (host): use the machine as a connection.</summary>
	public void TestUse( Connection c ) { Use( c ); }

	int[] _forceReels;
	int _forceWedge;

	/// <summary>Editor/test (host): the next paid spin lands on these items (1-8), the next bonus spin on this wedge (1-20).</summary>
	public void TestForce( int a, int b, int c, int wedge )
	{
		if ( a > 0 ) _forceReels = new int[] { a, b, c };
		_forceWedge = wedge;
	}

	/// <summary>Editor/test (client): open or close the paytable for this machine.</summary>
	public void TestPaytable( bool open )
	{
		WOLPaytableUi.Ensure( Scene );
		WOLPaytableUi.H.Machine = this;
		WOLPaytableUi.H.Open = open;
		WOLPaytableUi.H.Version++;
	}

	/// <summary>Editor/test (host): give bonus spins.</summary>
	public void TestGiveBonus( int spins ) { if ( !IsProxy ) { BonusSpins += spins; BonusMusicRpc( true ); Lights = 1; } }
}

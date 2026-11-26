// Wheel of Luck for s&box - config, paytable logic and the DarkRP wallet bridge.
// Ported from Blue's Slots "Wheel of Luck" for Garry's Mod (blues_slots_config.lua, wheel_of_luck/init.lua),
// MIT License, Copyright (c) 2020 In Memory of <CODE BLUE>. See WHEELOFLUCK_NOTICE.txt.
//
// Server owners: the host writes data/wheelofluck/config.json (in the game's data folder) with the defaults the
// first time a machine spawns. Edit it and respawn the machines (or restart) to apply. See Docs/WHEELOFLUCK.md.
using System.Text.Json.Nodes;

/// <summary>Everything a server owner can change. Same names and defaults as the GMod config.</summary>
public sealed class WOLConfig
{
	/// <summary>Seconds nobody else can use a machine after someone spun it, and before a player can switch machines.</summary>
	public long AntiTheftTime = 30;
	/// <summary>Added to the jackpot on every paid spin.</summary>
	public long JackpotIncreasePerSpin = 500;
	/// <summary>After a jackpot is won it resets to a random amount between these.</summary>
	public long JackpotResetMin = 100;
	public long JackpotResetMax = 2000000;
	/// <summary>Price of one spin (bonus spins are free).</summary>
	public long PricePerSpin = 500;
	/// <summary>Shown before amounts on the machine and paytable.</summary>
	public string CurrencyIcon = "$";
	/// <summary>When the first two reels match, the chance (0-100) that the third is forced to match too.</summary>
	public long ThirdReelMatchChance = 25;
	/// <summary>Tickets per reel symbol: bonus, raspberry, coins, diamond, bar2, bar, seven, nothing.</summary>
	public List<long> ItemChance = WOLDefaults.Chances();
	// Payouts
	public long ThreeBonus = 25000;
	public long TwoBonus = 10000;
	public long OneBonus = 5000;
	public long ThreeRaspberry = 2500;
	public long ThreeCoins = 10000;
	public long TwoCoins = 5000;
	public long ThreeDiamonds = 20000;
	public long TwoDiamonds = 7500;
	public long OneDiamonds = 1000;
	public long ThreeBar2 = 1750;
	public long ThreeBar = 1000;
	public long ThreeSeven = 3500;
	/// <summary>The 19 cash wedges of the bonus wheel (wedge 1, the jackpot, is not in the list).</summary>
	public List<long> BonusWheel = WOLDefaults.Wheel();

	public static string[] Keys() => new string[] { "AntiTheftTime", "JackpotIncreasePerSpin", "JackpotResetMin", "JackpotResetMax",
		"PricePerSpin", "ThirdReelMatchChance", "ThreeBonus", "TwoBonus", "OneBonus", "ThreeRaspberry", "ThreeCoins", "TwoCoins",
		"ThreeDiamonds", "TwoDiamonds", "OneDiamonds", "ThreeBar2", "ThreeBar", "ThreeSeven" };

	public long Get( string key )
	{
		switch ( key )
		{
			case "AntiTheftTime": return AntiTheftTime;
			case "JackpotIncreasePerSpin": return JackpotIncreasePerSpin;
			case "JackpotResetMin": return JackpotResetMin;
			case "JackpotResetMax": return JackpotResetMax;
			case "PricePerSpin": return PricePerSpin;
			case "ThirdReelMatchChance": return ThirdReelMatchChance;
			case "ThreeBonus": return ThreeBonus;
			case "TwoBonus": return TwoBonus;
			case "OneBonus": return OneBonus;
			case "ThreeRaspberry": return ThreeRaspberry;
			case "ThreeCoins": return ThreeCoins;
			case "TwoCoins": return TwoCoins;
			case "ThreeDiamonds": return ThreeDiamonds;
			case "TwoDiamonds": return TwoDiamonds;
			case "OneDiamonds": return OneDiamonds;
			case "ThreeBar2": return ThreeBar2;
			case "ThreeBar": return ThreeBar;
			case "ThreeSeven": return ThreeSeven;
		}
		return 0;
	}

	public void Set( string key, long v )
	{
		switch ( key )
		{
			case "AntiTheftTime": AntiTheftTime = v; break;
			case "JackpotIncreasePerSpin": JackpotIncreasePerSpin = v; break;
			case "JackpotResetMin": JackpotResetMin = v; break;
			case "JackpotResetMax": JackpotResetMax = v; break;
			case "PricePerSpin": PricePerSpin = v; break;
			case "ThirdReelMatchChance": ThirdReelMatchChance = v; break;
			case "ThreeBonus": ThreeBonus = v; break;
			case "TwoBonus": TwoBonus = v; break;
			case "OneBonus": OneBonus = v; break;
			case "ThreeRaspberry": ThreeRaspberry = v; break;
			case "ThreeCoins": ThreeCoins = v; break;
			case "TwoCoins": TwoCoins = v; break;
			case "ThreeDiamonds": ThreeDiamonds = v; break;
			case "TwoDiamonds": TwoDiamonds = v; break;
			case "OneDiamonds": OneDiamonds = v; break;
			case "ThreeBar2": ThreeBar2 = v; break;
			case "ThreeBar": ThreeBar = v; break;
			case "ThreeSeven": ThreeSeven = v; break;
		}
	}

	/// <summary>The config as JSON (also how the host sends it to clients).</summary>
	public string ToJson( bool pretty )
	{
		var nl = pretty ? "\n\t" : "";
		var sb = new System.Text.StringBuilder();
		sb.Append( "{" );
		foreach ( var k in Keys() )
		{
			sb.Append( nl ); sb.Append( '"' ); sb.Append( k ); sb.Append( "\": " ); sb.Append( Get( k ) ); sb.Append( "," );
		}
		sb.Append( nl ); sb.Append( "\"CurrencyIcon\": " ); sb.Append( Quote( CurrencyIcon ) ); sb.Append( "," );
		sb.Append( nl ); sb.Append( "\"ItemChance\": [" ); sb.Append( string.Join( ", ", ItemChance ) ); sb.Append( "]," );
		sb.Append( nl ); sb.Append( "\"BonusWheel\": [" ); sb.Append( string.Join( ", ", BonusWheel ) ); sb.Append( "]" );
		sb.Append( pretty ? "\n}\n" : "}" );
		return sb.ToString();
	}

	static string Quote( string s )
	{
		var sb = new System.Text.StringBuilder( "\"" );
		foreach ( var ch in s ?? "" )
		{
			if ( ch == '"' || ch == (char)92 ) { sb.Append( (char)92 ); sb.Append( ch ); }
			else if ( ch < ' ' ) sb.Append( ' ' );
			else sb.Append( ch );
		}
		sb.Append( '"' );
		return sb.ToString();
	}

	public static WOLConfig FromJson( string text )
	{
		var c = new WOLConfig();
		if ( string.IsNullOrWhiteSpace( text ) ) return c;
		JsonObject o = null;
		try { o = Json.ParseToJsonObject( text ); }
		catch ( Exception e ) { Log.Warning( "Wheel of Luck: bad config.json: " + e.Message ); return c; }
		if ( o == null ) return c;
		foreach ( var k in Keys() )
		{
			if ( !o.ContainsKey( k ) || o[k] == null ) continue;
			var v = ParseLong( o[k].ToString(), long.MinValue );
			if ( v != long.MinValue ) c.Set( k, v );
			else Log.Warning( "Wheel of Luck: config.json: bad number for " + k );
		}
		if ( o.ContainsKey( "CurrencyIcon" ) && o["CurrencyIcon"] != null ) c.CurrencyIcon = o["CurrencyIcon"].ToString();
		var chance = Longs( o, "ItemChance" );
		if ( chance.Count == 8 ) c.ItemChance = chance;
		else if ( chance.Count > 0 ) Log.Warning( "Wheel of Luck: config.json: ItemChance needs 8 numbers" );
		var wheel = Longs( o, "BonusWheel" );
		if ( wheel.Count == 19 ) c.BonusWheel = wheel;
		else if ( wheel.Count > 0 ) Log.Warning( "Wheel of Luck: config.json: BonusWheel needs 19 numbers" );
		if ( c.JackpotResetMax < c.JackpotResetMin ) c.JackpotResetMax = c.JackpotResetMin;
		return c;
	}

	public static long ParseLong( string text, long fallback )
	{
		if ( string.IsNullOrWhiteSpace( text ) ) return fallback;
		try { return (long)Math.Round( double.Parse( text.Trim(), System.Globalization.NumberStyles.Float ) ); }
		catch { return fallback; }
	}

	static List<long> Longs( JsonObject o, string key )
	{
		var list = new List<long>();
		if ( !o.ContainsKey( key ) || o[key] == null ) return list;
		var arr = o[key] as JsonArray;
		if ( arr == null ) return list;
		foreach ( var n in arr )
		{
			var v = n == null ? long.MinValue : ParseLong( n.ToString(), long.MinValue );
			if ( v != long.MinValue ) list.Add( Math.Max( 0, v ) );
		}
		return list;
	}

	/// <summary>Host: reads data/wheelofluck/config.json, writing the defaults first if it doesn't exist.</summary>
	public static WOLConfig LoadHost()
	{
		const string path = "wheelofluck/config.json";
		try
		{
			if ( !FileSystem.Data.FileExists( path ) )
			{
				FileSystem.Data.CreateDirectory( "wheelofluck" );
				FileSystem.Data.WriteAllText( path, new WOLConfig().ToJson( true ) );
				Log.Info( "Wheel of Luck: wrote default config to data/" + path );
			}
			return FromJson( FileSystem.Data.ReadAllText( path ) );
		}
		catch ( Exception e )
		{
			Log.Warning( "Wheel of Luck: couldn't read data/" + path + ": " + e.Message );
			return new WOLConfig();
		}
	}
}

public static class WOLDefaults
{
	public static List<long> Chances() => new List<long> { 1, 50, 30, 10, 60, 70, 55, 65 };
	public static List<long> Wheel() => new List<long> { 100, 200, 400, 1000, 2500, 5000, 7500, 10000, 15000, 20000, 30000,
		40000, 60000, 75000, 100000, 150000, 175000, 200000, 300000 };
	public static string[] ItemNames() => new string[] { "", "bonus", "raspberry", "coins", "diamond", "bar2", "bar", "seven", "nothing" };
}

/// <summary>A winning line: '*' matches anything. Lines of the same family can't both pay.</summary>
public sealed class WOLCombo
{
	public int Family;
	public string A, B, C;
	public long Cash;
	public int Spins;
	public string Sound;
}

public sealed class WOLReward
{
	public long Cash;
	public int Spins;
	public bool R1, R2, R3;
	public string Sound;
}

public static class WOLPaytable
{
	static WOLCombo Combo( int family, string a, string b, string c, long cash, int spins, string sound )
		=> new WOLCombo { Family = family, A = a, B = b, C = c, Cash = cash, Spins = spins, Sound = sound };

	/// <summary>The GMod winning combinations, in the same order (the first match of a family wins).</summary>
	public static List<WOLCombo> Combos( WOLConfig cfg )
	{
		const string win = "sounds/wheelofluck/win_small.sound";
		var l = new List<WOLCombo>();
		l.Add( Combo( 1, "bonus", "bonus", "bonus", cfg.ThreeBonus, 3, null ) );
		l.Add( Combo( 1, "bonus", "bonus", "*", cfg.TwoBonus, 2, win ) );
		l.Add( Combo( 1, "bonus", "*", "bonus", cfg.TwoBonus, 2, win ) );
		l.Add( Combo( 1, "*", "bonus", "bonus", cfg.TwoBonus, 2, win ) );
		l.Add( Combo( 1, "bonus", "*", "*", cfg.OneBonus, 1, win ) );
		l.Add( Combo( 1, "*", "bonus", "*", cfg.OneBonus, 1, win ) );
		l.Add( Combo( 1, "*", "*", "bonus", cfg.OneBonus, 1, win ) );
		l.Add( Combo( 2, "raspberry", "raspberry", "raspberry", cfg.ThreeRaspberry, 0, win ) );
		l.Add( Combo( 3, "coins", "coins", "coins", cfg.ThreeCoins, 0, win ) );
		l.Add( Combo( 3, "coins", "coins", "*", cfg.TwoCoins, 0, win ) );
		l.Add( Combo( 3, "*", "coins", "coins", cfg.TwoCoins, 0, win ) );
		l.Add( Combo( 4, "diamond", "diamond", "diamond", cfg.ThreeDiamonds, 0, win ) );
		l.Add( Combo( 4, "diamond", "*", "diamond", cfg.TwoDiamonds, 0, win ) );
		l.Add( Combo( 4, "*", "diamond", "diamond", cfg.TwoDiamonds, 0, win ) );
		l.Add( Combo( 4, "diamond", "diamond", "*", cfg.TwoDiamonds, 0, win ) );
		l.Add( Combo( 4, "diamond", "*", "*", cfg.OneDiamonds, 0, win ) );
		l.Add( Combo( 4, "*", "diamond", "*", cfg.OneDiamonds, 0, win ) );
		l.Add( Combo( 4, "*", "*", "diamond", cfg.OneDiamonds, 0, win ) );
		l.Add( Combo( 5, "bar2", "bar2", "bar2", cfg.ThreeBar2, 0, win ) );
		l.Add( Combo( 6, "bar", "bar", "bar", cfg.ThreeBar, 0, win ) );
		l.Add( Combo( 7, "seven", "seven", "seven", cfg.ThreeSeven, 0, win ) );
		return l;
	}

	/// <summary>Reward for three reel items (1-8). Port of WOL_GetWinningItems.</summary>
	public static WOLReward Evaluate( WOLConfig cfg, int item1, int item2, int item3 )
	{
		var names = WOLDefaults.ItemNames();
		var a = names[Math.Clamp( item1, 1, 8 )];
		var b = names[Math.Clamp( item2, 1, 8 )];
		var c = names[Math.Clamp( item3, 1, 8 )];
		var r = new WOLReward();
		var families = new List<int>();
		foreach ( var v in Combos( cfg ) )
		{
			bool m1 = v.A == "*" || v.A == a;
			bool m2 = v.B == "*" || v.B == b;
			bool m3 = v.C == "*" || v.C == c;
			if ( !m1 || !m2 || !m3 || families.Contains( v.Family ) ) continue;
			r.Cash += v.Cash;
			r.Spins += v.Spins;
			if ( v.A != "*" ) r.R1 = true;
			if ( v.B != "*" ) r.R2 = true;
			if ( v.C != "*" ) r.R3 = true;
			r.Sound = v.Sound;
			families.Add( v.Family );
		}
		return r;
	}

	/// <summary>A reel item 1-8 drawn by the configured tickets.</summary>
	public static int RandomItem( WOLConfig cfg )
	{
		long total = 0;
		foreach ( var t in cfg.ItemChance ) total += Math.Max( 0, t );
		if ( total <= 0 ) return 8;
		var pick = (long)(Game.Random.Float( 0f, 1f ) * total);
		for ( int i = 0; i < cfg.ItemChance.Count; i++ )
		{
			pick -= Math.Max( 0, cfg.ItemChance[i] );
			if ( pick < 0 ) return i + 1;
		}
		return 8;
	}

	public static string Money( WOLConfig cfg, long amount ) => cfg.CurrencyIcon + Commas( amount );

	/// <summary>1234567 -> "1,234,567" (GMod's comma_value).</summary>
	public static string Commas( long amount )
	{
		var digits = Math.Abs( amount ).ToString();
		var sb = new System.Text.StringBuilder();
		for ( int i = 0; i < digits.Length; i++ )
		{
			if ( i > 0 && (digits.Length - i) % 3 == 0 ) sb.Append( ',' );
			sb.Append( digits[i] );
		}
		return (amount < 0 ? "-" : "") + sb.ToString();
	}
}

/// <summary>Wallet state shared with the DarkRP bridge (WOLDarkRP.csx). Host only.</summary>
public sealed class WOLMoneyState
{
	/// <summary>Connections that have a DarkRP wallet, and their money (refreshed every frame by the bridge).</summary>
	public readonly HashSet<Guid> Known = new HashSet<Guid>();
	public readonly Dictionary<Guid, long> Balances = new Dictionary<Guid, long>();
	/// <summary>Payments the bridge still has to make with RP.AddMoney.</summary>
	public readonly List<Connection> PendingConn = new List<Connection>();
	public readonly List<long> PendingAmount = new List<long>();
	/// <summary>When the bridge last ran (RealTime); 0 = never, so DarkRP isn't loaded.</summary>
	public float LastSeen;
	/// <summary>The object the bridge was added to.</summary>
	public GameObject BridgeHost;
}

/// <summary>
/// Spins cost and pay DarkRP money (DarkRP wallet only). DarkRP is a soft dependency: the only file that names
/// DarkRP types is WOLDarkRP.csx. Without DarkRP that file stays unloaded, the bridge never runs and the machine
/// refuses to spin; everything else still loads.
/// </summary>
public static class WOLMoney
{
	public static readonly WOLMoneyState H = new WOLMoneyState();

	public static void Reset()
	{
		H.Known.Clear();
		H.Balances.Clear();
		H.PendingConn.Clear();
		H.PendingAmount.Clear();
		H.LastSeen = 0;
		H.BridgeHost = null;
	}

	/// <summary>Host: makes sure the bridge runs (adds it to this object if the DarkRP bridge type exists).</summary>
	public static void EnsureBridge( GameObject host )
	{
		if ( host == null || RealTime.Now - H.LastSeen < 1f ) return;
		if ( TypeLibrary.GetType( "RPPlayer" ) == null ) return; // DarkRP isn't loaded
		if ( H.BridgeHost.IsValid() ) return;
		try { host.Components.Create( TypeLibrary.GetType( "WOLDarkRPWallet" ) ); H.BridgeHost = host; }
		catch ( Exception e ) { Log.Warning( "Wheel of Luck: couldn't start the DarkRP bridge: " + e.Message ); }
	}

	public static bool Running() => H.LastSeen > 0 && RealTime.Now - H.LastSeen < 2f;

	public static bool Available( Connection c ) => c != null && Running() && H.Known.Contains( c.Id );

	public static long Balance( Connection c ) => Available( c ) && H.Balances.ContainsKey( c.Id ) ? H.Balances[c.Id] : 0;

	public static bool CanAfford( Connection c, long amount ) => amount <= 0 || Balance( c ) >= amount;

	/// <summary>Adds money (negative takes it). The bridge pays it with DarkRP's RP.AddMoney (hooks and saving run).</summary>
	public static bool Add( Connection c, long amount )
	{
		if ( amount == 0 ) return true;
		if ( !Available( c ) ) return false;
		H.PendingConn.Add( c );
		H.PendingAmount.Add( amount );
		H.Balances[c.Id] = Math.Max( 0, Balance( c ) + amount );
		return true;
	}
}

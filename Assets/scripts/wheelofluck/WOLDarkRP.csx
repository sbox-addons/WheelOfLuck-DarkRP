// Wheel of Luck for s&box - DarkRP wallet bridge.
// This is the only file that uses DarkRP types (RP.Of / RP.Money / RP.AddMoney from the DarkRP addon's RPPlayer.csx).
// When the DarkRP addon isn't loaded this file stays pending ("requires unavailable type 'RP'"); that is expected and
// harmless: the machine then tells players it needs DarkRP.
//
// Scripts can't declare interfaces, abstract classes or call each other through reflection, so the bridge talks to
// the rest of the addon through WOLMoney's shared state: every frame on the host it applies queued payments with
// RP.AddMoney and refreshes the wallet cache. A machine adds this component to itself on the host.

[Title( "Wheel of Luck - DarkRP Wallet" ), Hide]
public sealed class WOLDarkRPWallet : Component
{
	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		if ( TypeLibrary.GetType( "RPPlayer" ) == null ) { Enabled = false; return; }
		var h = WOLMoney.H;
		// Payments first, so the cache below already includes them.
		while ( h.PendingConn.Count > 0 )
		{
			var c = h.PendingConn[0];
			var amount = h.PendingAmount[0];
			h.PendingConn.RemoveAt( 0 );
			h.PendingAmount.RemoveAt( 0 );
			if ( c != null && RP.Of( c ) != null ) RP.AddMoney( c, amount );
		}
		h.Known.Clear();
		h.Balances.Clear();
		var conns = Connection.All.ToList();
		if ( Connection.Local != null && !conns.Contains( Connection.Local ) ) conns.Add( Connection.Local );
		foreach ( var c in conns )
		{
			if ( RP.Of( c ) == null ) continue;
			h.Known.Add( c.Id );
			h.Balances[c.Id] = RP.Money( c );
		}
		h.LastSeen = RealTime.Now;
	}
}

using ExitGames.Client.Photon;

public class PassAction : MainPhaseAction
{
    
    public PassAction()
    {
    }

    public PassAction(byte[] bytes)
    {
        Deserialize(bytes);
    }

    public override void Execute(TurnStateMachine stateMachine)
    {
        stateMachine.PassTurn();
    }

    public override void Deserialize(byte[] bytes)
    {
    }

    public override byte[] Serialize()
    {
        // === DCGO-CUSTOM:replay begin ===
        return System.Array.Empty<byte>();
        // === DCGO-CUSTOM:replay end ===
    }
}

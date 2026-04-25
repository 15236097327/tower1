namespace MazeTD.Shared
{
    public enum PacketType : byte
    {
        // C2S
        C2S_JoinRoom = 0x01,
        C2S_PlaceTower = 0x02,
        C2S_RemoveTower = 0x03,
        C2S_SendTroops = 0x04,
        C2S_UpgradeTower = 0x05,
        C2S_ReadyToggle = 0x06,
        C2S_ChatMessage = 0x07,
        C2S_Heartbeat = 0x08,
        C2S_SelectRole = 0x09,  // 加这行
        C2S_ReadyConfirm = 0x0A,  // 加这行
        C2S_CancelReady = 0x0B,
        C2S_ChooseBranch = 0x0C,
        S2C_BranchAck = 0x91,
        C2S_UnlockTroop = 0x0D,
        S2C_UnlockTroopAck = 0x92,
        S2C_TroopTree = 0x93,
        S2C_TowerDestroyed = 0x94,
        // S2C
        S2C_JoinAck = 0x81,
        S2C_GameStart = 0x82,
        S2C_StateSync = 0x83,
        S2C_PlaceAck = 0x84,
        S2C_RemoveAck = 0x85,
        S2C_SendTroopsAck = 0x86,
        S2C_PathUpdate = 0x87,
        S2C_ResourceUpdate = 0x88,
        S2C_BaseHpUpdate = 0x89,
        S2C_WaveStart = 0x8A,
        S2C_GameOver = 0x8B,
        S2C_ChatMessage = 0x8C,
        S2C_PlayerDisconnect = 0x8D,
        S2C_Error = 0x8E,
        S2C_Heartbeat = 0x8F,
        S2C_RoomState = 0x90,  // 加这行
    }
}
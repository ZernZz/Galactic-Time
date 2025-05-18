using Unity.Netcode;
using UnityEngine;
using Unity.Collections;
[System.Serializable]
public struct PlayerResult : INetworkSerializable
{
    public FixedString32Bytes playerName;
    public int boxCount;

    public PlayerResult(string name, int box)
    {
        playerName = name;
        boxCount = box;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref playerName);
        serializer.SerializeValue(ref boxCount);
    }
}
using UnityEngine;
using Unity.Netcode;

public class PickupBox : NetworkBehaviour
{
    [Header("Pickup Settings")] public Transform holdPoint;
    public float pickupRange = 2f;
    public KeyCode pickupKey = KeyCode.E;

    private NetworkObject heldBox = null;

    void Update()
    {
        if (!IsOwner) return;

        // ถ้าโดนแย่งไป ให้เคลียร์กล่องทันที
        if (heldBox != null && !heldBox.IsOwner)
        {
            Debug.Log("[Client] Lost ownership of heldBox → clearing");
            heldBox = null;
        }

        if (Input.GetKeyDown(pickupKey))
        {
            if (heldBox == null)
                TryPickupBox();
            else
                DropBoxServerRpc();
        }

        if (heldBox != null)
            heldBox.transform.position = holdPoint.position;
    }


    void TryPickupBox()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, pickupRange);
        foreach (var hit in hits)
        {
            if (hit.CompareTag("BoxMiniB"))
            {
                NetworkObject netObj = hit.GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    PickupBoxServerRpc(netObj.NetworkObjectId);
                    break;
                }
            }
        }
    }



    [ServerRpc]
    void PickupBoxServerRpc(ulong boxId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(boxId, out NetworkObject box))
        {
            box.TrySetParent(NetworkObject); // Attach the box to the player
            box.GetComponent<Rigidbody>().isKinematic = true;

            // Update server's heldBox as well as the client’s
            heldBox = box; 

            SetHeldBoxClientRpc(boxId);
        }
    }

    [ClientRpc]
    void SetHeldBoxClientRpc(ulong boxId)
    {
        if (!IsOwner) return;
        heldBox = NetworkManager.Singleton.SpawnManager.SpawnedObjects[boxId];
    }

    [ServerRpc]
    void DropBoxServerRpc()
    {
        if (heldBox == null) return;

        ulong boxId = heldBox.NetworkObjectId;
        DropBoxByIdServerRpc(boxId);
        heldBox = null; // Clear the server-side heldBox
    }

    [ServerRpc]
    void DropBoxByIdServerRpc(ulong boxId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(boxId, out NetworkObject box))
        {
            box.TryRemoveParent();
            box.GetComponent<Rigidbody>().isKinematic = false;

            // แจ้ง Client ให้ล้าง heldBox
            ClearHeldBoxClientRpc();

            Debug.Log($"[Server] Dropped box {boxId} from client {OwnerClientId}");
        }
    }

    [ClientRpc]
    void ClearHeldBoxClientRpc()
    {
        if (!IsOwner) return;

        heldBox = null;
        Debug.Log($"[Client] Cleared heldBox on client {OwnerClientId}");
    }
}
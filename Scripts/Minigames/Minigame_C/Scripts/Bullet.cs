using Unity.Netcode;
using UnityEngine;

public class Bullet : NetworkBehaviour
{
    public ulong ownerClientId; // 🟡 ใครเป็นคนยิง

    public float speed = 30f;
    public float lifeTime = 3f;

    private void Start()
    {
        if (IsServer)
        {
            Destroy(gameObject, lifeTime);
        }
    }

    private void Update()
    {
        transform.position += transform.forward * speed * Time.deltaTime;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        if (other.CompareTag("Meteor"))
        {
            Meteor meteor = other.GetComponent<Meteor>();
            if (meteor != null)
            {
                meteor.TakeHit(ownerClientId); // ส่ง ClientId ของคนยิง
            }

            Destroy(gameObject);
        }
    }
}

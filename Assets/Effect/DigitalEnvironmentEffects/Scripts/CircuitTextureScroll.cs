using UnityEngine;

public class CircuitTextureScroll : MonoBehaviour
{
    ParticleSystem ps;
    Material particleMaterial;
    Vector2 partOffset;
    public float partTextureScrollSpeed = -1;

    void Start()
    {
        ps = GetComponent<ParticleSystem>();
        particleMaterial = GetComponent<Renderer>().material;
        partOffset = particleMaterial.HasProperty("_BaseMap")
            ? particleMaterial.GetTextureOffset("_BaseMap")
            : particleMaterial.mainTextureOffset;
    }

    void LateUpdate()
    {
        if (particleMaterial == null)
            return;

        partOffset.y += partTextureScrollSpeed * Time.deltaTime;
        if (partOffset.y <= -1f)
            partOffset.y = 0f;

        if (particleMaterial.HasProperty("_BaseMap"))
            particleMaterial.SetTextureOffset("_BaseMap", partOffset);
        if (particleMaterial.HasProperty("_MainTex"))
            particleMaterial.SetTextureOffset("_MainTex", partOffset);
        particleMaterial.mainTextureOffset = partOffset;
    }
}

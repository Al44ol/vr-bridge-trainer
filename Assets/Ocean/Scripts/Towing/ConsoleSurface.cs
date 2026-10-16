// ============================================================
// ConsoleSurface.cs — Поверхность пульта под точкой (запрос 19.4а-3: рукоятка и тумблер — на панели, не в воздухе)
//
// У модели мостика МОСТИКИ коллайдеров нет, поэтому на время поиска на части рулевой стойки (ShipTypeSwitcher.keptConsole,
// forceKeep) ставятся временные MeshCollider (как Setup 19.2 Berth ищет галерею); луч — сверху вниз по вертикали судна;
// берётся верхняя поверхность (не триггер, внутри Ship, не XR Origin и не сама рукоятка). После поиска коллайдеры убираются.
// ============================================================
using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;

public sealed class ConsoleSurface : IDisposable
{
    private readonly List<Collider> temp = new List<Collider>();
    private readonly Transform ship;

    public ConsoleSurface(Transform ship, IEnumerable<Renderer> console)
    {
        this.ship = ship;
        if (console != null)
            foreach (Renderer r in console)
            {
                if (r == null || r.GetComponent<Collider>() != null) continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mc = r.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                temp.Add(mc);
            }
        Physics.SyncTransforms();
    }

    /// <summary>Верхняя поверхность под точкой world (луч от above м над ней вниз на above + below м по up).</summary>
    public bool Find(Vector3 world, Vector3 up, float above, float below, Transform ignore, out Vector3 point, out Vector3 normal)
    {
        point = world;
        normal = up;
        RaycastHit[] hits = Physics.RaycastAll(world + up * above, -up, above + below, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit h in hits)
        {
            Transform ht = h.collider.transform;
            if (ship != null && !ht.IsChildOf(ship)) continue;
            if (ignore != null && ht.IsChildOf(ignore)) continue;
            if (h.collider.GetComponentInParent<XROrigin>() != null) continue;
            point = h.point;
            normal = h.normal;
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        foreach (Collider c in temp) if (c != null) UnityEngine.Object.DestroyImmediate(c);
        temp.Clear();
    }
}

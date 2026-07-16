using UnityEngine;
namespace KickTheBuddy.Physics
{
    [DisallowMultipleComponent]
    public sealed class ElasticitySpringDemoMotion2D : MonoBehaviour
    {
        [SerializeField] Transform startPoint;
        [SerializeField] Transform endPoint;
        [Min(0f)] [SerializeField] float horizontalAmplitude=.55f;
        [Min(0f)] [SerializeField] float verticalAmplitude=.35f;
        [Min(.01f)] [SerializeField] float cyclesPerSecond=.35f;
        [SerializeField] float phaseOffset;
        [Range(0f,1f)] [SerializeField] float startPointCounterMotion=.12f;
        Vector3 startRestPosition,endRestPosition;
        void Awake()=>CacheRestPose();
        void OnEnable()=>CacheRestPose();
        void Update()
        {
            if(startPoint==null||endPoint==null)return;
            float phase=Time.time*cyclesPerSecond*Mathf.PI*2f+phaseOffset;
            Vector3 offset=new Vector3(Mathf.Sin(phase)*horizontalAmplitude,Mathf.Sin(phase*.63f+.8f)*verticalAmplitude,0f);
            endPoint.position=endRestPosition+offset;
            startPoint.position=startRestPosition-offset*startPointCounterMotion;
        }
        void CacheRestPose()
        {
            if(startPoint!=null)startRestPosition=startPoint.position;
            if(endPoint!=null)endRestPosition=endPoint.position;
        }
    }
}

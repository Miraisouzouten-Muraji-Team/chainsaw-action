using UnityEngine;
using UnityEngine.UI;

namespace Project.Tutorial
{
    // 説明専用。実際のPlayer/Enemyの状態やダメージは変更しない。
    internal sealed class TutorialCombatDemo
    {
        internal readonly RectTransform root;
        private readonly RectTransform actor, weapon, enemy, hpFill, meterFill;
        private readonly GameObject hpRoot, meterRoot;
        private readonly Text status, meterText, arrow;
        private readonly Image actorImage, enemyImage;
        private readonly Color normal = new Color(.75f,.95f,1f);
        internal static bool Supports(TutorialDemo d) => d == TutorialDemo.Accelerator ||
            d == TutorialDemo.FloorBonus || d == TutorialDemo.WeakCombo ||
            d == TutorialDemo.EnemyDig || d == TutorialDemo.AirEnemyDig;

        internal TutorialCombatDemo(Transform parent, Font font)
        {
            root = TutorialView.Rect("Combat Demo", parent, Vector2.zero);
            TutorialView.Place(root, Vector2.zero, Vector2.one);
            Bar("Floor", root, new Vector2(.02f,.08f), new Vector2(.98f,.1f), Color.cyan);
            actor = TutorialView.Rect("Player", root, new Vector2(16,32));
            actor.pivot = new Vector2(.5f,0);
            actorImage = actor.gameObject.AddComponent<Image>(); actorImage.color = normal;
            var head = TutorialView.Rect("Head", actor, new Vector2(12,12));
            head.anchoredPosition = new Vector2(0,24); head.gameObject.AddComponent<Image>().color = normal;
            weapon = TutorialView.Rect("Chainsaw", actor, new Vector2(32,5));
            weapon.pivot = new Vector2(0,.5f); weapon.anchoredPosition = new Vector2(5,15);
            weapon.gameObject.AddComponent<Image>().color = new Color(1,.65f,.15f);
            enemy = TutorialView.Rect("Enemy", root, new Vector2(24,30));
            enemy.pivot = new Vector2(.5f,0); enemyImage = enemy.gameObject.AddComponent<Image>();
            hpRoot = TutorialView.Rect("Enemy HP", root, Vector2.zero).gameObject;
            TutorialView.Place((RectTransform)hpRoot.transform,new Vector2(.53f,.76f),new Vector2(.92f,.8f));
            hpRoot.AddComponent<Image>().color = new Color(.35f,.06f,.06f);
            hpFill = Bar("Remaining",hpRoot.transform,Vector2.zero,Vector2.one,Color.green);
            meterRoot = TutorialView.Rect("RPM",root,Vector2.zero).gameObject;
            TutorialView.Place((RectTransform)meterRoot.transform,new Vector2(.03f,.01f),new Vector2(.36f,.06f));
            meterRoot.AddComponent<Image>().color = new Color(.08f,.08f,.15f);
            meterFill = Bar("RPM Fill",meterRoot.transform,Vector2.zero,Vector2.one,new Color(.6f,.45f,1));
            meterText = TutorialView.Label(root,font,"RPM Value",13,Color.white);
            TutorialView.Place(meterText.rectTransform,new Vector2(.02f,.1f),new Vector2(.35f,.23f));
            status = TutorialView.Label(root,font,"Demo Status",14,Color.white);
            TutorialView.Place(status.rectTransform,new Vector2(.02f,.84f),new Vector2(.98f,1));
            arrow = TutorialView.Label(root,font,"Knockback Direction",24,Color.yellow);
            arrow.text = "→"; arrow.rectTransform.sizeDelta = new Vector2(45,30);
            root.gameObject.SetActive(false);
        }
        private static RectTransform Bar(string name,Transform parent,Vector2 min,Vector2 max,Color color)
        {
            var rect = TutorialView.Rect(name,parent,Vector2.zero);
            TutorialView.Place(rect,min,max); var image = rect.gameObject.AddComponent<Image>();
            image.color = color; image.raycastTarget = false; return rect;
        }
        private static void Position(RectTransform rect,float x,float y)
        { rect.anchorMin = rect.anchorMax = new Vector2(x,y); rect.anchoredPosition = Vector2.zero; }
        private static bool Pulse(float phase,float start,float duration) => phase >= start && phase < start + duration;
        private void Meter(float value,float maximum)
        {
            meterFill.anchorMax = new Vector2(Mathf.Clamp01(value/maximum),1);
            meterText.text = $"回転数 {value:F1}";
        }

        internal void Tick(TutorialPage page,float phase,int cycle,out bool pressed,out string button)
        {
            float first = Mathf.Clamp(page.firstPress,.05f,.7f);
            float end = Mathf.Clamp(page.landing,first+.15f,.98f);
            float t = Mathf.InverseLerp(first,end,phase);
            float max = Mathf.Max(1,page.demoMaxRPM), min = Mathf.Clamp(page.demoMinRPM,0,max);
            float pulse = Mathf.Clamp(page.triggerPressDuration,.01f,.12f);
            pressed = Pulse(phase,first,pulse); button = "ZL";
            bool hasEnemy = page.demo != TutorialDemo.Accelerator;
            enemy.gameObject.SetActive(hasEnemy);
            hpRoot.SetActive(page.demo == TutorialDemo.EnemyDig || page.demo == TutorialDemo.AirEnemyDig || page.demo == TutorialDemo.WeakCombo);
            meterRoot.SetActive(page.demo != TutorialDemo.WeakCombo); meterText.gameObject.SetActive(meterRoot.activeSelf);
            arrow.gameObject.SetActive(false);
            actorImage.color = normal; enemyImage.color = new Color(.9f,.35f,.25f);
            actor.localRotation = Quaternion.identity; weapon.localRotation = Quaternion.Euler(0,0,-45);
            Position(actor,.22f,.1f); Position(enemy,.6f,.1f);
            hpFill.anchorMax = Vector2.one;
            switch(page.demo)
            {
                case TutorialDemo.Accelerator:
                    button = "ZR";
                    float release = Mathf.Clamp(page.customRelease,first+.1f,.85f);
                    pressed = phase >= first && phase < release;
                    float rise = Mathf.Clamp01((phase-first)/(release-first));
                    float fall = Mathf.Clamp01((phase-release)/(1-release));
                    float rpm = phase < release ? Mathf.Lerp(min,max,rise) : Mathf.Lerp(max,min,fall);
                    // RPMの積分を位置へ変換し、上昇/下降と実際のデモ速度を一致させる。
                    float area;
                    if(phase < first) area = min*phase;
                    else if(phase < release)
                    { float dt=phase-first; area=min*first+min*dt+(max-min)*dt*dt/(2*(release-first)); }
                    else
                    { float dt=phase-release; area=min*first+(min+max)*.5f*(release-first)+max*dt-(max-min)*dt*dt/(2*(1-release)); }
                    float total=min*first+(min+max)*.5f*(1-first);
                    Position(actor,.12f+.74f*area/Mathf.Max(.001f,total),.1f);
                    weapon.localRotation=Quaternion.Euler(0,0,-65);
                    actor.localRotation=Quaternion.Euler(0,0,-12);
                    status.text = pressed ? "ZR長押し：回転数・速度が上昇" : phase < first ? "床に食い込んだ状態" : "ZRを離す：回転数・速度が低下";
                    Meter(rpm,max);
                    break;
                case TutorialDemo.FloorBonus:
                    bool bonus = max > page.demoBonusThreshold;
                    float travel = Mathf.InverseLerp(first,Mathf.Min(end,first+Mathf.Max(.01f,page.demoBonusTravelDuration)),phase);
                    float after = Mathf.Clamp(page.demoAfterBonusRPM,min,max);
                    Meter(phase < first ? max : Mathf.Lerp(after,min,t),max);
                    Position(enemy,.48f,.1f);
                    Position(actor,bonus ? Mathf.Lerp(.18f,.72f,travel)+.12f*t : .18f+.3f*t,.1f);
                    weapon.localRotation=Quaternion.Euler(0,0,-65);
                    bool evading = bonus && phase>=first && travel<1;
                    actorImage.color = evading ? new Color(.1f,1,1,.35f) : normal;
                    status.text = phase<first ? $"回転数 {page.demoBonusThreshold:F0} 超で食い込み" : !bonus ? "回転数不足：通常の食い込み" : evading ? "ボーナス！ 回避しながら高速移動" : "回転数消費後も床食い込みを継続";
                    break;
                case TutorialDemo.WeakCombo:
                    button="X";
                    float second=Mathf.Clamp(page.secondPress,first+.06f,.84f);
                    float third=Mathf.Clamp(page.thirdPress,second+.06f,.93f);
                    float duration=Mathf.Min(pulse,Mathf.Min(second-first,third-second)*.45f);
                    pressed=Pulse(phase,first,duration)||Pulse(phase,second,duration)||Pulse(phase,third,duration);
                    int hits=phase>=third?3:phase>=second?2:phase>=first?1:0;
                    float hitTime=hits==3?third:hits==2?second:first;
                    float swing=hits==0?0:Mathf.Clamp01((phase-hitTime)/Mathf.Max(.01f,duration));
                    Position(actor,.43f,.1f);
                    weapon.localRotation=Quaternion.Euler(0,0,pressed?Mathf.Lerp(hits%2==0?-65:65,hits%2==0?65:-65,swing):0);
                    enemyImage.color=pressed?Color.white:new Color(.9f,.35f,.25f);
                    Position(enemy,.6f+hits*.035f,.1f);
                    hpFill.anchorMax=new Vector2(1-hits*.25f,1);
                    status.text=$"X × {hits} / 3";
                    break;
                case TutorialDemo.EnemyDig:
                case TutorialDemo.AirEnemyDig:
                    bool air=page.demo==TutorialDemo.AirEnemyDig;
                    float starting = cycle%2==0 ? Mathf.Lerp(min,max,.22f) : Mathf.Lerp(min,max,.98f);
                    int ticks=phase<first?0:Mathf.Clamp(1+Mathf.FloorToInt(t*4),1,4);
                    float current=Mathf.Max(min,starting-ticks*2f);
                    float damage=Mathf.Lerp(.05f,.18f,starting/max);
                    Vector2 knock = Vector2.ClampMagnitude(page.demoKnockback,.25f)*(ticks/4f);
                    float enemyX=Mathf.Clamp(.6f+knock.x,.4f,.88f);
                    float enemyY=Mathf.Clamp((air?.4f:.1f)+(air?knock.y:0),.1f,.56f);
                    Position(enemy,enemyX,enemyY);
                    Position(actor,enemyX-.17f,enemyY);
                    weapon.localRotation=Quaternion.identity;
                    actorImage.color=phase>=first?Color.cyan:normal;
                    hpFill.anchorMax=new Vector2(Mathf.Max(0,1-ticks*damage),1);
                    Meter(current,max);
                    status.text=(cycle%2==0?"低回転：ダメージ小":"高回転：ダメージ大")+(air?" ／ 空中の敵":"");
                    arrow.gameObject.SetActive(air && ticks>0);
                    Position(arrow.rectTransform,enemyX,enemyY+.3f);
                    arrow.rectTransform.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(page.demoKnockback.y,page.demoKnockback.x)*Mathf.Rad2Deg);
                    break;
            }
        }
    }
}

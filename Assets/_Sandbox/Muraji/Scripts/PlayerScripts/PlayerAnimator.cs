using UnityEngine;

/* Playerのアニメーションをセットして動かす */
public class PlayerAnimator : MonoBehaviour
{
    Animator animator;
    [SerializeField] ChainsawAttack attackHitBox; // チェーンソーのヒットボックス

    // animatorの取得
    void Awake()
    {
        animator = GetComponent<Animator>();
    }

    /* ↓Playerアニメーション↓ */
    public void SetSpeed(float speed) // 移動
    {
        animator.SetFloat("Speed", Mathf.Abs(speed));
    }

    //public void PlayAttack() // 3段攻撃
    //{
    //    animator.SetTrigger("Attack");
    //}

    public void PlaySlash() // 単発攻撃
    {
        animator.SetTrigger("Slash");
    }

    public void PlaySlash2() // 2段攻撃
    {
        animator.SetTrigger("Slash2");
    }

    public void PlaySlash3() // 3段攻撃
    {
        animator.SetTrigger("Slash3");
    }

    public void PlayWedgie() // 食い込み
    {
        animator.SetTrigger("Wedgie");
    }

    public void PlayJump() // ジャンプ
    {
        animator.SetTrigger("Jump");
    }

    public void EnableAttackHitBox() // 攻撃判定有効化
    {
        attackHitBox.EnableHitBox();
    }

    public void DisableAttackHitBox() // 攻撃判定無効化
    {
        attackHitBox.DisableHitBox();
    }
}

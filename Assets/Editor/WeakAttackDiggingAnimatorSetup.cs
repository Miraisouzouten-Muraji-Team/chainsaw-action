using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

internal static class WeakAttackDiggingAnimatorSetup
{
    private const string MENU = "Tools/Chainsaw/弱攻撃と食い込みのAnimator遷移を修正";
    private const string ACTION_TRIGGER = "WeakActionRequested";
    private const string ACTION_TARGET = "WeakActionTarget";
    private const int IDLE_TARGET = 0;
    private const int DIGGING_TARGET = 3;
    private const int WALL_DIGGING_TARGET = 4;
    private const int CEILING_DIGGING_TARGET = 5;
    private const int ENEMY_DIGGING_TARGET = 6;

    // 選んだプレイヤーやControllerで、弱攻撃終了時の待機・食い込み遷移を修正する。
    [MenuItem(MENU)]
    private static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Playを停止してから、弱攻撃と食い込みのAnimator遷移を修正してください。");
            return;
        }

        GameObject selected = Selection.activeGameObject;
        PlayerAnimator player = selected != null ? selected.GetComponentInParent<PlayerAnimator>(true) : null;
        if (player == null && selected != null)
            player = selected.GetComponentInChildren<PlayerAnimator>(true);

        AnimatorController controller = FindSelectedController(Selection.activeObject, player);
        if (controller == null || controller.layers.Length == 0)
        {
            Debug.LogWarning("Hierarchyでプレイヤーを選ぶか、Projectでプレイヤーが使うAnimator Controllerを選んでください。", Selection.activeObject);
            return;
        }

        AnimatorControllerLayer layer = controller.layers[0];
        AnimatorStateMachine root = layer.stateMachine;
        // Controllerを直接選んだ場合は、現在のプレイヤーのステート名から開始・終了先を見つける。
        SerializedObject settings = player != null ? new SerializedObject(player) : null;
        string groundPath = settings == null ? layer.name + ".Slash2" : settings.FindProperty("slash1State")?.stringValue;
        string airPath = settings == null ? layer.name + ".Slash3" : settings.FindProperty("airSlashState")?.stringValue;
        string idlePath = settings == null ? layer.name + ".Idle" : settings.FindProperty("idleState")?.stringValue;
        string diggingPath = settings == null ? layer.name + ".Digging" : settings.FindProperty("diggingState")?.stringValue;
        AnimatorState ground = FindState(root, layer.name, groundPath);
        AnimatorState air = string.IsNullOrWhiteSpace(airPath) ? ground : FindState(root, layer.name, airPath);
        AnimatorState idle = FindState(root, layer.name, idlePath);
        AnimatorState digging = FindState(root, layer.name, diggingPath);
        if (ground == null || air == null || idle == null || digging == null)
        {
            Debug.LogError("弱攻撃・Idle・Diggingのステートが見つかりません。ステート名が異なる場合は、Hierarchyのプレイヤーを選んで実行してください。", controller);
            return;
        }

        AnimatorControllerParameter[] parameters = controller.parameters;
        if (parameters.Any(parameter =>
            (parameter.name == ACTION_TRIGGER && parameter.type != AnimatorControllerParameterType.Trigger) ||
            (parameter.name == ACTION_TARGET && parameter.type != AnimatorControllerParameterType.Int)))
        {
            Debug.LogError("WeakActionRequestedはTrigger、WeakActionTargetはIntにしてください。", controller);
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        const string undoName = "弱攻撃と食い込みのAnimator遷移を修正";
        Undo.SetCurrentGroupName(undoName);
        Undo.RegisterCompleteObjectUndo(controller, undoName);
        Undo.RegisterCompleteObjectUndo(root, undoName);

        if (!parameters.Any(parameter => parameter.name == ACTION_TRIGGER))
            controller.AddParameter(ACTION_TRIGGER, AnimatorControllerParameterType.Trigger);
        if (!parameters.Any(parameter => parameter.name == ACTION_TARGET))
            controller.AddParameter(ACTION_TARGET, AnimatorControllerParameterType.Int);

        var destinations = new List<(AnimatorState State, int Target, float Duration)>
        {
            (idle, IDLE_TARGET, 0.10f),
            (digging, DIGGING_TARGET, 0.12f)
        };
        AddSurfaceDestination(destinations, root, layer.name, settings, "wallDiggingState", WALL_DIGGING_TARGET, digging);
        AddSurfaceDestination(destinations, root, layer.name, settings, "ceilingDiggingState", CEILING_DIGGING_TARGET, digging);
        AddSurfaceDestination(destinations, root, layer.name, settings, "enemyDiggingState", ENEMY_DIGGING_TARGET, digging);

        foreach (var destination in destinations)
        {
            AnimatorStateTransition[] anyStateReturns = root.anyStateTransitions.Where(transition =>
                transition.destinationState == destination.State && HasActionTarget(transition, destination.Target)).ToArray();
            AnimatorStateTransition previous = anyStateReturns.FirstOrDefault(transition => !transition.mute);

            EnsureReturnTransition(ground, destination.State, destination.Target, destination.Duration, previous, undoName);
            if (air != ground)
                EnsureReturnTransition(air, destination.State, destination.Target, destination.Duration, previous, undoName);

            foreach (AnimatorStateTransition transition in anyStateReturns)
                MuteTransition(transition, undoName);
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssetIfDirty(controller);
        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log($"{controller.name}を更新しました。{ground.name} / {air.name} からIdle・食い込みへの直接遷移を設定しました。", controller);
    }

    // 選択したプレイヤー、Controller、またはAnimator内のステートや矢印から対象を特定する。
    private static AnimatorController FindSelectedController(Object selected, PlayerAnimator player)
    {
        if (player != null)
        {
            Animator animator = player.GetComponent<Animator>();
            return animator != null ? animator.runtimeAnimatorController as AnimatorController : null;
        }

        if (selected is AnimatorController controller) return controller;
        if (selected is AnimatorState || selected is AnimatorStateMachine || selected is AnimatorTransitionBase)
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GetAssetPath(selected));

        return null;
    }

    // PlayerAnimatorに設定されたフルパスと同じステートを探す。
    private static AnimatorState FindState(AnimatorStateMachine machine, string prefix, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(prefix + ".", System.StringComparison.Ordinal))
            return null;

        foreach (ChildAnimatorState child in machine.states)
        {
            if (prefix + "." + child.state.name == path) return child.state;
        }

        foreach (ChildAnimatorStateMachine child in machine.stateMachines)
        {
            AnimatorState found = FindState(child.stateMachine, prefix + "." + child.stateMachine.name, path);
            if (found != null) return found;
        }

        return null;
    }

    // 面別の食い込みステートを使っている場合は、その直接遷移も準備する。
    private static void AddSurfaceDestination(List<(AnimatorState State, int Target, float Duration)> destinations,
        AnimatorStateMachine root, string layerName, SerializedObject settings, string property, int target,
        AnimatorState defaultDigging)
    {
        AnimatorState destination = settings != null
            ? FindState(root, layerName, settings.FindProperty(property)?.stringValue)
            : root.anyStateTransitions.FirstOrDefault(transition => HasActionTarget(transition, target))?.destinationState;
        if (destination != null && destination != defaultDigging)
            destinations.Add((destination, target, 0.12f));
    }

    // 弱攻撃の終了通知と遷移先を指定する、共通のAnimator条件を判定する。
    private static bool HasActionTarget(AnimatorStateTransition transition, int target)
    {
        AnimatorCondition[] conditions = transition.conditions;
        return conditions.Any(condition => condition.parameter == ACTION_TRIGGER &&
            condition.mode == AnimatorConditionMode.If) &&
            conditions.Any(condition => condition.parameter == ACTION_TARGET &&
                condition.mode == AnimatorConditionMode.Equals && condition.threshold == target);
    }

    // 攻撃ステート自身の出口を設定し、従来のブレンド時間を引き継ぐ。
    private static void EnsureReturnTransition(AnimatorState attack, AnimatorState destination, int target,
        float defaultDuration, AnimatorStateTransition previous, string undoName)
    {
        AnimatorStateTransition[] existing = attack.transitions;
        AnimatorStateTransition transition = existing.FirstOrDefault(candidate =>
            candidate.destinationState == destination && candidate.conditions.Length == 2 && HasActionTarget(candidate, target));

        if (transition == null)
        {
            Undo.RegisterCompleteObjectUndo(attack, undoName);
            transition = attack.AddTransition(destination, false);
            transition.AddCondition(AnimatorConditionMode.If, 0, ACTION_TRIGGER);
            transition.AddCondition(AnimatorConditionMode.Equals, target, ACTION_TARGET);
            transition.hasFixedDuration = previous == null || previous.hasFixedDuration;
            transition.duration = previous != null ? previous.duration : defaultDuration;
            transition.offset = previous != null ? previous.offset : 0f;
            EditorUtility.SetDirty(attack);
        }
        else
        {
            Undo.RegisterCompleteObjectUndo(transition, undoName);
        }

        transition.hasExitTime = false;
        transition.canTransitionToSelf = false;
        transition.mute = false;
        EditorUtility.SetDirty(transition);

        foreach (AnimatorStateTransition other in existing)
        {
            // 同じステートを複数の食い込み面で共用する場合は、別の遷移先番号を残す。
            if (other != transition && other.destinationState == destination &&
                (HasActionTarget(other, target) || !other.conditions.Any(condition => condition.parameter == ACTION_TRIGGER)))
            {
                MuteTransition(other, undoName);
            }
        }
    }

    // 競合する遷移を削除せず無効化し、必要ならUndoで元に戻せるようにする。
    private static void MuteTransition(AnimatorStateTransition transition, string undoName)
    {
        if (transition.mute) return;
        Undo.RegisterCompleteObjectUndo(transition, undoName);
        transition.mute = true;
        EditorUtility.SetDirty(transition);
    }
}

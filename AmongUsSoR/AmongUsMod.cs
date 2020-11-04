using System.IO;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using RogueLibsCore;
using UnityEngine;
using UnityEngine.Networking;
using System.Threading;

namespace AmongUsSoR
{
	[BepInPlugin(pluginGuid, pluginName, pluginVersion)]
	public class AmongUsMod : BaseUnityPlugin
	{
		public const string pluginGuid = "streetsofrogue.abbysssal.amongus";
		public const string pluginName = "Among Us Mod";
		public const string pluginVersion = "0.3";

		public static Sprite killSprite;
		public static Sprite sabotageSprite;
		public static Sprite ventSprite;

		public static AmongUsMod plugin;

		public static CustomAbility abilityKill;
		public static CustomAbility abilitySabotage;
		public static CustomAbility abilityVent;

		public void Awake()
		{
			plugin = this;

			RoguePatcher patcher = new RoguePatcher(this, GetType());
			patcher.Postfix(typeof(AudioHandler), nameof(AudioHandler.SetupDics));
			// RogueUtilities.ConvertToAudioClip doesn't work!

			killSprite = RogueUtilities.ConvertToSprite(Properties.Resources.Kill);
			ventSprite = RogueUtilities.ConvertToSprite(Properties.Resources.Vent);
			sabotageSprite = RogueUtilities.ConvertToSprite(Properties.Resources.Sabotage);

			abilityKill = RogueLibs.CreateCustomAbility("AmongUsKill", killSprite, true,
				new CustomNameInfo("[Among Us] Impostor",
				null, null, null, null,
				"[Among Us] Предатель"),
				new CustomNameInfo("Kill people by snapping their necks and hide in vents.",
				null, null, null, null,
				"Убивай людей сворачивая их шеи и прячьтесь в вентиляции."),
				item =>
				{
					item.lowCountThreshold = 100;
					item.initCount = 0;
					item.stackable = true;
				});
			abilityKill.CostInCharacterCreation = 20;

			abilityKill.IndicatorCheck = IndicatorCheck;
			abilityKill.OnPressed = OnPressedAbility;

			abilityKill.RechargeInterval = (item, _) => item.invItemCount > 0 ? new WaitForSeconds(1) : null;
			abilityKill.Recharge = (item, myAgent) =>
			{
				if (item.invItemCount > 0 && myAgent.statusEffects.CanRecharge())
				{
					item.invItemCount--;
					if (item.invItemCount == 0)
					{
						myAgent.statusEffects.CreateBuffText("Recharged", myAgent.objectNetID);
						myAgent.gc.audioHandler.Play(myAgent, "Recharge");
						myAgent.inventory.buffDisplay.specialAbilitySlot.MakeUsable();
					}
				}
			};

			// Internal abilities, used only for special ability indicators

			abilityVent = RogueLibs.CreateCustomAbility("AmongUsVentInternal", ventSprite, false,
				new CustomNameInfo(""),
				new CustomNameInfo(""),
				_ => { });
			abilityVent.Available = false;
			abilityVent.AvailableInCharacterCreation = false;
			abilityVent.Unlocked = false;

			abilitySabotage = RogueLibs.CreateCustomAbility("AmongUsSabotageInternal", sabotageSprite, false,
				new CustomNameInfo(""),
				new CustomNameInfo(""),
				_ => { });
			abilitySabotage.Available = false;
			abilitySabotage.AvailableInCharacterCreation = false;
			abilitySabotage.Unlocked = false;
		}

		public static void AudioHandler_SetupDics(AudioHandler __instance)
		{
			string path = Path.Combine(Paths.CachePath, "AmongUs");
			if (!Directory.Exists(path)) Directory.CreateDirectory(path);

			string killClip = Path.Combine(path, "AudioKill.ogg");
			if (!File.Exists(killClip))
				File.WriteAllBytes(killClip, Properties.Resources.AudioKill);

			__instance.audioClipRealList.Add(__instance.audioClipDic["AmongUsKill"] = GetAudio(killClip));

			string ventClip = Path.Combine(path, "AudioVent.ogg");
			if (!File.Exists(ventClip))
				File.WriteAllBytes(ventClip, Properties.Resources.AudioVent);

			__instance.audioClipRealList.Add(__instance.audioClipDic["AmongUsVent"] = GetAudio(ventClip));
		}
		public static AudioClip GetAudio(string path)
		{
			UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip("file:///" + path, AudioType.OGGVORBIS);
			request.SendWebRequest();
			while (!request.isDone) Thread.Sleep(10);
			return DownloadHandlerAudioClip.GetContent(request);
		}

		public static Agent GetKillTarget(Agent myAgent)
		{
			if (myAgent.ghost) return null;

			Agent closestAgent = null;
			float closestDistance = float.MaxValue;
			foreach (GameObject obj in myAgent.interactionHelper.TriggerList)
			{
				if (!obj.CompareTag("AgentSprite")) continue;
				Agent agent = obj.GetComponent<ObjectSprite>().agent;
				if (agent.dead || agent.ghost || agent.hologram || !agent.go.activeSelf) continue;
				if (agent.mechEmpty || agent.mechFilled) continue;

				float distance = Vector2.Distance(myAgent.tr.position, agent.tr.position);
				if (distance < closestDistance)
				{
					closestAgent = agent;
					closestDistance = distance;
				}
			}
			return closestAgent;
		}
		public static PlayfieldObject GetVentTarget(Agent myAgent)
		{
			if (myAgent.agentSpriteTransform.localScale.x != 1f) return null;

			PlayfieldObject closestVent = null;
			float closestDistance = float.MaxValue;
			foreach (GameObject obj in myAgent.interactionHelper.TriggerList)
			{
				if (!obj.CompareTag("ObjectRealSprite")) continue;
				ObjectReal vent = obj.name.Contains("ExtraSprite")
					? obj.transform.parent.transform.parent.GetComponent<ObjectReal>()
					: obj.transform.parent.GetComponent<ObjectReal>();
				if (!(vent is GasVent) && !(vent is AirConditioner)) continue;
				if (vent.fire != null || vent.destroying || vent.tempNoInteractions || !vent.go.activeSelf) continue;

				float distance = Vector2.Distance(myAgent.tr.position, vent.tr.position);
				if (distance < closestDistance)
				{
					closestVent = vent;
					closestDistance = distance;
				}
			}
			return closestVent;
		}
		public static PlayfieldObject IndicatorCheck(InvItem item, Agent myAgent)
		{
			if (!myAgent.statusEffects.CanShowSpecialAbilityIndicator()) return null;

			Agent victim = item.invItemCount < 1 ? GetKillTarget(myAgent) : null;
			PlayfieldObject vent = GetVentTarget(myAgent);

			if (victim == null && vent == null) return null; // SABOTAGE
			if (victim != null && vent != null)
			{
				if (Vector2.Distance(victim.tr.position, myAgent.tr.position) <= Vector2.Distance(vent.tr.position, myAgent.tr.position)) vent = null;
				else victim = null;
			}

			plugin.StartCoroutine(ResetIconCoroutine(myAgent));
			if (victim != null) // KILL
			{
				myAgent.specialAbility = abilityKill.Id;
				return victim;
			}
			else // VENT
			{
				myAgent.specialAbility = abilityVent.Id;
				return vent;
			}
		}
		public static IEnumerator ResetIconCoroutine(Agent myAgent)
		{
			yield return null;
			myAgent.specialAbility = abilityKill.Id;
		}

		public static void BecomeHidden(StatusEffects __instance, ObjectReal vent)
		{
			bool wasHidden = __instance.agent.hiddenInObject != null;
			__instance.agent.oma.hidden = true;
			__instance.agent.hiddenInObject = vent;
			vent.agentHiding = __instance.agent;
			__instance.agent.tr.position = new Vector2(vent.tr.position.x, vent.tr.position.y + 0.24f);
			__instance.agent.rb.velocity = Vector2.zero;
			if (!__instance.agent.gc.consoleVersion)
				__instance.agent.EnableMouseboxes(false);
			__instance.agent.agentHitboxScript.shadow.GetComponent<MeshRenderer>().enabled = false;
			__instance.agent.SetInvisible(true);
			__instance.agent.objectSprite.RefreshRenderer();
			__instance.agent.objectMult.BecomeHidden();

			if (__instance.agent.isPlayer != 0 && __instance.agent.localPlayer)
			{
				__instance.agent.blockWalking = true;
				if (!wasHidden)
					__instance.StartCoroutine(WaitForAgentUnhide(__instance));
			}
		}
		public static IEnumerator WaitForAgentUnhide(StatusEffects __instance)
		{
			bool closeToPlayerPos = true;
			do
			{
				if (Vector2.Distance(__instance.agent.hiddenInObject.tr.position, __instance.agent.tr.position) > 0.32f)
					closeToPlayerPos = false;
				else
					yield return null;
			}
			while (closeToPlayerPos && __instance.agent.oma.hidden);
			if (__instance.agent.oma.hidden)
				__instance.BecomeNotHidden();
		}

		public static void OnPressedAbility(InvItem item, Agent myAgent)
		{
			bool client = item.gc.multiplayer && !item.gc.serverPlayer && myAgent.isPlayer == 0;
			PlayfieldObject target = IndicatorCheck(item, myAgent);
			if (target is Agent victim) // KILL
			{
				if (item.invItemCount > 0)
				{
					myAgent.gc.audioHandler.Play(myAgent, "CantDo");
					return;
				}
				victim.deathKiller = "Impostor";
				victim.deathMethod = "Kill";

				myAgent.tr.position = victim.tr.position;
				victim.statusEffects.SetupDeath(myAgent, client, true);
				myAgent.gc.audioHandler.Play(victim, "AmongUsKill", victim.objectMult.IsFromClient(), true);

				myAgent.inventory.buffDisplay.specialAbilitySlot.MakeNotUsable();
				item.invItemCount = 20;
			}
			else if (target is AirConditioner ac || target is GasVent vent) // VENT
			{
				List<ObjectReal> list = new List<ObjectReal>();
				foreach (ObjectReal obj in target.gc.objectRealList)
					if ((obj is GasVent || obj is AirConditioner) && obj.startingChunk == target.startingChunk && obj != target)
						list.Add(obj);

				if (list.Count < 1)
				{
					myAgent.gc.audioHandler.Play(myAgent, "CantDo");
					return;
				}
				int rnd = new System.Random().Next(list.Count);
				ObjectReal targetVent = list[rnd];
				myAgent.tr.position = targetVent.tr.position;
				myAgent.rb.velocity = Vector2.zero;
				myAgent.gc.audioHandler.Play(myAgent, "AmongUsVent", myAgent.objectMult.IsFromClient(), false);
				BecomeHidden(myAgent.statusEffects, targetVent);
			}
			else // SABOTAGE
			{
				myAgent.gc.audioHandler.Play(myAgent, "CantDo");
			}
		}

	}
}

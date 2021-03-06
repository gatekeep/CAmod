﻿#region Copyright & License Information
/*
 * Copyright 2007-2019 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Linq;
using OpenRA.GameRules;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.CA.Traits
{
	[Desc("This actor explodes when killed and the kill XP goes to the Spawner.")]
	public class SpawnedExplodesInfo : ExplodesInfo
	{
		public override object Create(ActorInitializer init) { return new SpawnedExplodes(this, init.Self); }
	}

	public class SpawnedExplodes : ConditionalTrait<SpawnedExplodesInfo>, INotifyKilled, INotifyDamage, INotifyCreated
	{
		readonly IHealth health;
		IFirepowerModifier[] firepowerModifiers;
		BuildingInfo buildingInfo;

		public SpawnedExplodes(SpawnedExplodesInfo info, Actor self)
			: base(info)
		{
			health = self.Trait<IHealth>();
			firepowerModifiers = self.TraitsImplementing<IFirepowerModifier>().ToArray();
		}

		void INotifyCreated.Created(Actor self)
		{
			buildingInfo = self.Info.TraitInfoOrDefault<BuildingInfo>();
		}

		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			if (IsTraitDisabled || !self.IsInWorld)
				return;

			if (self.World.SharedRandom.Next(100) > Info.Chance)
				return;

			if (!Info.DeathTypes.IsEmpty && !e.Damage.DamageTypes.Overlaps(Info.DeathTypes))
				return;

			var weapon = ChooseWeaponForExplosion(self);
			if (weapon == null)
				return;

			var source = Info.DamageSource == DamageSource.Self ? self : e.Attacker;
			if (weapon.Report != null && weapon.Report.Any())
				Game.Sound.Play(SoundType.World, weapon.Report.Random(source.World.SharedRandom), self.CenterPosition);

			var spawner = self.Trait<BaseSpawnerSlave>().Master;
			var damageModifiers = !spawner.IsDead ? spawner.TraitsImplementing<IFirepowerModifier>()
						.Select(a => a.GetFirepowerModifier()).ToArray() : new int[0];

			var args = new ProjectileArgs
			{
				Weapon = weapon,
				Facing = 0,
				CurrentMuzzleFacing = () => 0,

				DamageModifiers = !spawner.IsDead ? spawner.TraitsImplementing<IFirepowerModifier>()
						.Select(a => a.GetFirepowerModifier()).ToArray() : new int[0],

				InaccuracyModifiers = new int[0],

				RangeModifiers = new int[0],

				Source = self.CenterPosition,
				CurrentSource = () => self.CenterPosition,
				SourceActor = spawner,
				PassiveTarget = self.CenterPosition
			};

			if (Info.Type == ExplosionType.Footprint && buildingInfo != null)
			{
				var cells = buildingInfo.OccupiedTiles(self.Location);
				foreach (var c in cells)
					weapon.Impact(Target.FromPos(self.World.Map.CenterOfCell(c)), new WarheadArgs(args));

				return;
			}

			// Use .FromPos since this actor is killed. Cannot use Target.FromActor
			weapon.Impact(Target.FromPos(self.CenterPosition), new WarheadArgs(args));
		}

		WeaponInfo ChooseWeaponForExplosion(Actor self)
		{
			var armaments = self.TraitsImplementing<Armament>();
			if (!armaments.Any())
				return Info.WeaponInfo;

			// TODO: EmptyWeapon should be removed in favour of conditions
			var shouldExplode = !armaments.All(a => a.IsReloading);
			var useFullExplosion = self.World.SharedRandom.Next(100) <= Info.LoadedChance;
			return (shouldExplode && useFullExplosion) ? Info.WeaponInfo : Info.EmptyWeaponInfo;
		}

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			if (IsTraitDisabled || !self.IsInWorld)
				return;

			if (Info.DamageThreshold == 0)
				return;

			if (!Info.DeathTypes.IsEmpty && !e.Damage.DamageTypes.Overlaps(Info.DeathTypes))
				return;

			// Cast to long to avoid overflow when multiplying by the health
			var source = Info.DamageSource == DamageSource.Self ? self : e.Attacker;
			if (health.HP * 100L < Info.DamageThreshold * (long)health.MaxHP)
				self.World.AddFrameEndTask(w => self.Kill(source));
		}
	}
}

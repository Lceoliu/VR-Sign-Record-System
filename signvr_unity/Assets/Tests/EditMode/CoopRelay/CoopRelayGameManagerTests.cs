using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace SignVR.CoopRelay.Tests
{
    public sealed class CoopRelayGameManagerTests
    {
        private static readonly string[] ItemIds =
        {
            "stone_teal",
            "stone_coral",
            "stone_yellow",
            "key",
            "coolant"
        };

        private readonly List<GameObject> createdObjects =
            new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int index = createdObjects.Count - 1; index >= 0; index--)
            {
                UnityEngine.Object.DestroyImmediate(createdObjects[index]);
            }

            createdObjects.Clear();
        }

        [Test]
        public void FiveItems_RequireMatchingItemInStrictSocketOrder()
        {
            RelayFixture relay = CreateRelay();

            Assert.That(relay.Manager.RequiredItemCount, Is.EqualTo(5));
            Assert.That(relay.Manager.ExpectedItemId, Is.EqualTo(ItemIds[0]));

            Assert.That(relay.Sockets[1].TryAccept(relay.Items[1]), Is.False);
            Assert.That(relay.Sockets[0].TryAccept(relay.Items[1]), Is.False);
            Assert.That(relay.Manager.DockedCount, Is.Zero);

            for (int index = 0; index < ItemIds.Length; index++)
            {
                Assert.That(
                    relay.Sockets[index].TryAccept(relay.Items[index]),
                    Is.True,
                    $"Sequence item {index} should dock."
                );
                Assert.That(relay.Manager.DockedCount, Is.EqualTo(index + 1));
            }

            Assert.That(
                relay.Manager.Phase,
                Is.EqualTo(CoopRelayPhase.AwaitConsoleConfirmation)
            );
            Assert.That(relay.Manager.RoundComplete, Is.False);
            Assert.That(relay.Manager.ExpectedItemId, Is.Empty);
        }

        [Test]
        public void SharedStatus_DoesNotRevealThePrivateRunOrder()
        {
            RelayFixture relay = CreateRelay();

            Assert.That(relay.Manager.ExpectedItemId, Is.EqualTo(ItemIds[0]));
            Assert.That(
                relay.Manager.StatusMessage,
                Does.Contain("B: READ ORDER")
            );
            Assert.That(
                relay.Manager.StatusMessage,
                Does.Not.Contain(ItemIds[0]).IgnoreCase
            );
        }

        [Test]
        public void ConsoleConfirmation_IsRejectedUntilAllItemsAreCollected()
        {
            RelayFixture relay = CreateRelay();

            Assert.That(relay.Manager.ConfirmConsole(), Is.False);
            Assert.That(
                relay.Manager.Phase,
                Is.EqualTo(CoopRelayPhase.CollectItems)
            );

            DockAllItems(relay);

            Assert.That(relay.Manager.ConfirmConsole(), Is.True);
            Assert.That(
                relay.Manager.Phase,
                Is.EqualTo(CoopRelayPhase.AwaitStationReady)
            );
            Assert.That(relay.Manager.LeftReady, Is.False);
            Assert.That(relay.Manager.RightReady, Is.False);
        }

        [Test]
        public void BothStationsReady_CompleteRoundWhileOneStationDoesNot()
        {
            RelayFixture relay = CreateRelayReadyForStations();

            Assert.That(relay.Manager.PressLeftReady(), Is.True);
            Assert.That(relay.Manager.LeftReady, Is.True);
            Assert.That(relay.Manager.RightReady, Is.False);
            Assert.That(relay.Manager.RoundComplete, Is.False);
            Assert.That(
                relay.Manager.Phase,
                Is.EqualTo(CoopRelayPhase.AwaitStationReady)
            );
            Assert.That(relay.Manager.SynchronizationAttempts, Is.EqualTo(1));

            Assert.That(relay.Manager.PressRightReady(), Is.True);
            Assert.That(relay.Manager.LeftReady, Is.True);
            Assert.That(relay.Manager.RightReady, Is.True);
            Assert.That(relay.Manager.RoundComplete, Is.True);
            Assert.That(
                relay.Manager.Phase,
                Is.EqualTo(CoopRelayPhase.Complete)
            );
        }

        [Test]
        public void ResetRound_ClearsStateAndRestoresEveryRigidbodyToSpawn()
        {
            RelayFixture relay = CreateRelayReadyForStations();
            Assert.That(relay.Manager.PressLeftReady(), Is.True);

            var expectedPositions = new Vector3[relay.Items.Length];
            var expectedRotations = new Quaternion[relay.Items.Length];

            for (int index = 0; index < relay.Items.Length; index++)
            {
                CoopRelayItem item = relay.Items[index];
                Rigidbody itemRigidbody = item.ItemRigidbody;
                expectedPositions[index] = item.SpawnPosition;
                expectedRotations[index] = item.SpawnRotation;

                itemRigidbody.isKinematic = false;
                item.transform.SetPositionAndRotation(
                    new Vector3(20f + index, -3f, 8f),
                    Quaternion.Euler(30f, 60f, 90f)
                );
                itemRigidbody.linearVelocity = new Vector3(2f, 3f, 4f);
                itemRigidbody.angularVelocity = new Vector3(5f, 6f, 7f);
            }

            relay.Manager.ResetRound();

            Assert.That(
                relay.Manager.Phase,
                Is.EqualTo(CoopRelayPhase.CollectItems)
            );
            Assert.That(relay.Manager.DockedCount, Is.Zero);
            Assert.That(relay.Manager.LeftReady, Is.False);
            Assert.That(relay.Manager.RightReady, Is.False);
            Assert.That(relay.Manager.RoundComplete, Is.False);
            Assert.That(relay.Manager.SynchronizationAttempts, Is.Zero);
            Assert.That(relay.Manager.ExpectedItemId, Is.EqualTo(ItemIds[0]));

            for (int index = 0; index < relay.Items.Length; index++)
            {
                CoopRelayItem item = relay.Items[index];
                Rigidbody itemRigidbody = item.ItemRigidbody;

                Assert.That(item.IsDocked, Is.False);
                Assert.That(relay.Sockets[index].IsOccupied, Is.False);
                Assert.That(
                    Vector3.Distance(
                        item.transform.position,
                        expectedPositions[index]
                    ),
                    Is.LessThan(0.0001f)
                );
                Assert.That(
                    Quaternion.Angle(
                        item.transform.rotation,
                        expectedRotations[index]
                    ),
                    Is.LessThan(0.001f)
                );
                Assert.That(itemRigidbody.isKinematic, Is.False);
                Assert.That(
                    itemRigidbody.linearVelocity,
                    Is.EqualTo(Vector3.zero)
                );
                Assert.That(
                    itemRigidbody.angularVelocity,
                    Is.EqualTo(Vector3.zero)
                );
            }
        }

        private RelayFixture CreateRelayReadyForStations()
        {
            RelayFixture relay = CreateRelay();
            DockAllItems(relay);
            Assert.That(relay.Manager.ConfirmConsole(), Is.True);
            return relay;
        }

        private void DockAllItems(RelayFixture relay)
        {
            for (int index = 0; index < relay.Items.Length; index++)
            {
                Assert.That(
                    relay.Sockets[index].TryAccept(relay.Items[index]),
                    Is.True
                );
            }
        }

        private RelayFixture CreateRelay()
        {
            var items = new CoopRelayItem[ItemIds.Length];
            var sockets = new CoopRelaySocket[ItemIds.Length];

            for (int index = 0; index < ItemIds.Length; index++)
            {
                items[index] = CreateItem(
                    ItemIds[index],
                    new Vector3(index * 0.25f, 1f, -0.5f)
                );
                sockets[index] = CreateSocket(
                    ItemIds[index],
                    index,
                    new Vector3(index * 0.25f, 0.8f, 0.5f)
                );
            }

            CoopRelayGameManager manager = CreateObject("Manager")
                .AddComponent<CoopRelayGameManager>();
            manager.Configure(
                items,
                sockets,
                null,
                allowSoloDebug: true,
                standardReadyWindowSeconds: 6f,
                soloReadyWindowSeconds: 20f
            );

            return new RelayFixture(manager, items, sockets);
        }

        private CoopRelayItem CreateItem(string id, Vector3 spawnPosition)
        {
            GameObject itemObject = CreateObject($"Item {id}");
            itemObject.transform.SetPositionAndRotation(
                spawnPosition,
                Quaternion.Euler(0f, spawnPosition.x * 10f, 0f)
            );
            Rigidbody itemRigidbody = itemObject.AddComponent<Rigidbody>();
            CoopRelayItem item = itemObject.AddComponent<CoopRelayItem>();
            item.Configure(
                id,
                itemRigidbody,
                null,
                Array.Empty<Behaviour>()
            );
            return item;
        }

        private CoopRelaySocket CreateSocket(
            string acceptedId,
            int sequenceIndex,
            Vector3 snapPosition
        )
        {
            GameObject socketObject = CreateObject($"Socket {acceptedId}");
            socketObject.AddComponent<BoxCollider>();
            CoopRelaySocket socket =
                socketObject.AddComponent<CoopRelaySocket>();

            GameObject snapObject = CreateObject($"Snap {acceptedId}");
            snapObject.transform.SetParent(socketObject.transform, false);
            snapObject.transform.SetPositionAndRotation(
                snapPosition,
                Quaternion.Euler(0f, sequenceIndex * 15f, 0f)
            );

            socket.Configure(
                acceptedId,
                sequenceIndex,
                snapObject.transform,
                null,
                null
            );
            return socket;
        }

        private GameObject CreateObject(string name)
        {
            var gameObject = new GameObject(name);
            createdObjects.Add(gameObject);
            return gameObject;
        }

        private readonly struct RelayFixture
        {
            public RelayFixture(
                CoopRelayGameManager manager,
                CoopRelayItem[] items,
                CoopRelaySocket[] sockets
            )
            {
                Manager = manager;
                Items = items;
                Sockets = sockets;
            }

            public CoopRelayGameManager Manager { get; }
            public CoopRelayItem[] Items { get; }
            public CoopRelaySocket[] Sockets { get; }
        }
    }
}

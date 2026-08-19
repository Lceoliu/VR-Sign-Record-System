using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace SignVR.SortingGame.Tests
{
    public sealed class SortingGameTests
    {
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
        public void MatchingPiece_CompletesRoundAndResetRestoresSpawnState()
        {
            Vector3 spawnPosition = new Vector3(1f, 2f, 3f);
            PlacementPiece piece = CreatePiece("coral", spawnPosition);
            SortingGameManager manager = CreateObject("Manager")
                .AddComponent<SortingGameManager>();
            PlacementTarget target = CreateTarget(
                "coral",
                new Vector3(-2f, 1f, 4f),
                manager
            );

            manager.Configure(
                new[] { piece },
                new[] { target },
                null
            );

            Assert.That(target.TryAccept(piece), Is.True);
            Assert.That(piece.IsPlaced, Is.True);
            Assert.That(target.IsOccupied, Is.True);
            Assert.That(manager.PlacedCount, Is.EqualTo(1));
            Assert.That(manager.RoundComplete, Is.True);
            Assert.That(manager.ResetBelowHeight, Is.LessThan(-0.25f));
            Assert.That(
                piece.transform.position,
                Is.EqualTo(target.transform.GetChild(0).position)
            );
            Assert.That(piece.GetComponent<Rigidbody>().isKinematic, Is.True);

            manager.ResetRound();

            Assert.That(piece.IsPlaced, Is.False);
            Assert.That(target.IsOccupied, Is.False);
            Assert.That(manager.PlacedCount, Is.Zero);
            Assert.That(manager.RoundComplete, Is.False);
            Assert.That(piece.transform.position, Is.EqualTo(spawnPosition));
            Assert.That(piece.GetComponent<Rigidbody>().isKinematic, Is.False);

            Rigidbody resetRigidbody = piece.GetComponent<Rigidbody>();
            resetRigidbody.linearVelocity = new Vector3(2f, 3f, 4f);
            resetRigidbody.angularVelocity = new Vector3(1f, 2f, 3f);
            manager.ResetRound();

            Assert.That(resetRigidbody.linearVelocity, Is.EqualTo(Vector3.zero));
            Assert.That(resetRigidbody.angularVelocity, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void MismatchedPiece_IsRejectedWithoutChangingRoundState()
        {
            PlacementPiece piece = CreatePiece("blue", Vector3.one);
            SortingGameManager manager = CreateObject("Manager")
                .AddComponent<SortingGameManager>();
            PlacementTarget target = CreateTarget(
                "red",
                Vector3.zero,
                manager
            );

            manager.Configure(
                new[] { piece },
                new[] { target },
                null
            );

            Assert.That(target.TryAccept(piece), Is.False);
            Assert.That(piece.IsPlaced, Is.False);
            Assert.That(target.IsOccupied, Is.False);
            Assert.That(manager.PlacedCount, Is.Zero);
            Assert.That(manager.RoundComplete, Is.False);
        }

        private PlacementPiece CreatePiece(string id, Vector3 spawnPosition)
        {
            GameObject pieceObject = CreateObject($"Piece {id}");
            pieceObject.transform.position = spawnPosition;
            Rigidbody pieceRigidbody = pieceObject.AddComponent<Rigidbody>();
            PlacementPiece piece = pieceObject.AddComponent<PlacementPiece>();
            piece.Configure(
                id,
                pieceRigidbody,
                null,
                Array.Empty<Behaviour>()
            );
            return piece;
        }

        private PlacementTarget CreateTarget(
            string acceptedId,
            Vector3 snapPosition,
            SortingGameManager manager
        )
        {
            GameObject targetObject = CreateObject($"Target {acceptedId}");
            targetObject.AddComponent<BoxCollider>();
            PlacementTarget target = targetObject.AddComponent<PlacementTarget>();

            GameObject snapObject = CreateObject($"Snap {acceptedId}");
            snapObject.transform.SetParent(targetObject.transform, false);
            snapObject.transform.position = snapPosition;

            target.Configure(
                acceptedId,
                snapObject.transform,
                null,
                Color.gray,
                Color.green,
                manager
            );
            return target;
        }

        private GameObject CreateObject(string name)
        {
            var gameObject = new GameObject(name);
            createdObjects.Add(gameObject);
            return gameObject;
        }
    }
}

// Copyright SlopArena Contributors. MIT License.

#include "ProjectileActor.h"
#include "ProjectileState.h"
#include "Components/SphereComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/StaticMesh.h"
#include "GameFramework/ProjectileMovementComponent.h"
#include "SlopArena/SlopArena.h"

AProjectileActor::AProjectileActor()
{
	PrimaryActorTick.bCanEverTick = true;
	PrimaryActorTick.TickGroup = TG_PrePhysics;

	// Collision sphere
	CollisionComponent = CreateDefaultSubobject<USphereComponent>(TEXT("Collision"));
	CollisionComponent->SetSphereRadius(50.0f);
	CollisionComponent->SetCollisionProfileName(TEXT("Projectile"));
	CollisionComponent->SetGenerateOverlapEvents(true);
	SetRootComponent(CollisionComponent);

	// Visual mesh (will be set by blueprint)
	MeshComponent = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Mesh"));
	MeshComponent->SetupAttachment(RootComponent);
	MeshComponent->SetCollisionEnabled(ECollisionEnabled::NoCollision);

	// Set lifespan so it auto-destroys if cleanup fails
	InitialLifeSpan = 10.0f;
}

void AProjectileActor::BeginPlay()
{
	Super::BeginPlay();
}

void AProjectileActor::Tick(float DeltaTime)
{
	Super::Tick(DeltaTime);
	// Visual only — actual position is driven by ProjectileManager/NetworkPrediction
}

void AProjectileActor::InitFromState(const FProjectileState& State)
{
	ProjectileId = State.ProjectileId;
	CasterId = State.CasterEntityId;
	Damage = State.Damage;
	KickForce = State.KnockbackForce;
	KickUpward = State.KnockbackUpward;
	Speed = State.Speed;

	SetActorLocation(State.Position);
	SetActorRotation(State.Direction.Rotation());

	// Size the collision sphere to match
	CollisionComponent->SetSphereRadius(State.Radius);

	// Scale mesh to match
	if (MeshComponent)
	{
		float MeshScale = State.Radius / 50.0f;
		MeshComponent->SetWorldScale3D(FVector(MeshScale));
	}

	UE_LOG(LogSlopArenaCombat, Verbose, TEXT("ProjectileActor spawned: id=%llu caster=%llu dmg=%.0f speed=%.0f"),
		ProjectileId, CasterId, Damage, Speed);
}

void AProjectileActor::SyncToState(const FProjectileState& State)
{
	// Interpolate position from simulation state
	SetActorLocation(State.Position);
	SetActorRotation(State.Direction.Rotation());
}

void AProjectileActor::OnHit(AActor* OtherActor)
{
	UE_LOG(LogSlopArenaCombat, Log, TEXT("Projectile %llu hit %s"), ProjectileId, *GetNameSafe(OtherActor));
	Destroy();
}

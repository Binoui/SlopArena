// Copyright SlopArena Contributors. MIT License.

#include "SlopArenaCharacter.h"
#include "AbilitySystemComponent.h"
#include "AttributeSet.h"
#include "Abilities/GameplayAbility.h"
#include "Components/InputComponent.h"
#include "EnhancedInputComponent.h"
#include "EnhancedInputSubsystems.h"
#include "GameFramework/PlayerController.h"
#include "GameFramework/SpringArmComponent.h"
#include "Camera/CameraComponent.h"
#include "NetworkPredictionProxy.h"
#include "SlopArena/SlopArena.h"

ASlopArenaCharacter::ASlopArenaCharacter(const FObjectInitializer& ObjectInitializer)
	: Super(ObjectInitializer)
{
	PrimaryActorTick.bCanEverTick = true;

	// WoW-style camera setup
	SpringArmComponent = CreateDefaultSubobject<USpringArmComponent>(TEXT("SpringArm"));
	SpringArmComponent->SetupAttachment(RootComponent);
	SpringArmComponent->TargetArmLength = 800.0f;
	SpringArmComponent->bUsePawnControlRotation = true;
	SpringArmComponent->bInheritPitch = true;
	SpringArmComponent->bInheritYaw = true;
	SpringArmComponent->bInheritRoll = false;
	SpringArmComponent->bDoCollisionTest = true;

	CameraComponent = CreateDefaultSubobject<UCameraComponent>(TEXT("Camera"));
	CameraComponent->SetupAttachment(SpringArmComponent, USpringArmComponent::SocketName);
	CameraComponent->bUsePawnControlRotation = false;

	// GAS
	AbilitySystemComponent = CreateDefaultSubobject<UAbilitySystemComponent>(TEXT("AbilitySystemComponent"));
	AbilitySystemComponent->SetIsReplicated(true);
	AbilitySystemComponent->SetReplicationMode(EGameplayEffectReplicationMode::Mixed);

	AttributeSet = CreateDefaultSubobject<UAttributeSet>(TEXT("AttributeSet"));
}

UAbilitySystemComponent* ASlopArenaCharacter::GetAbilitySystemComponent() const
{
	return AbilitySystemComponent;
}

void ASlopArenaCharacter::BeginPlay()
{
	Super::BeginPlay();

	// Add default abilities
	if (AbilitySystemComponent && DefaultAbilities.Num() > 0)
	{
		for (auto Ability : DefaultAbilities)
		{
			AbilitySystemComponent->GiveAbility(
				FGameplayAbilitySpec(Ability, 1, INDEX_NONE, this)
			);
		}
		AbilitySystemComponent->InitAbilityActorInfo(this, this);
	}
}

void ASlopArenaCharacter::Tick(float DeltaTime)
{
	Super::Tick(DeltaTime);
	// NetworkPrediction tick will be set up in Phase 4
}

void ASlopArenaCharacter::SetupPlayerInputComponent(UInputComponent* PlayerInputComponent)
{
	Super::SetupPlayerInputComponent(PlayerInputComponent);

	if (APlayerController* PC = Cast<APlayerController>(GetController()))
	{
		if (UEnhancedInputLocalPlayerSubsystem* Subsystem = ULocalPlayer::GetSubsystem<UEnhancedInputLocalPlayerSubsystem>(PC->GetLocalPlayer()))
		{
			Subsystem->AddMappingContext(DefaultMappingContext, 0);
		}
	}

	if (UEnhancedInputComponent* EnhancedInput = Cast<UEnhancedInputComponent>(PlayerInputComponent))
	{
		EnhancedInput->BindAction(MoveAction, ETriggerEvent::Triggered, this, &ASlopArenaCharacter::OnMove);
		EnhancedInput->BindAction(JumpAction, ETriggerEvent::Started, this, &ASlopArenaCharacter::OnJumpStart);
		EnhancedInput->BindAction(JumpAction, ETriggerEvent::Completed, this, &ASlopArenaCharacter::OnJumpEnd);
		EnhancedInput->BindAction(DashAction, ETriggerEvent::Started, this, &ASlopArenaCharacter::OnDash);
		EnhancedInput->BindAction(AttackAction, ETriggerEvent::Started, this, &ASlopArenaCharacter::OnLightAttack);
		EnhancedInput->BindAction(Ability1Action, ETriggerEvent::Started, this, &ASlopArenaCharacter::OnAbility1);
		EnhancedInput->BindAction(Ability2Action, ETriggerEvent::Started, this, &ASlopArenaCharacter::OnAbility2);
		EnhancedInput->BindAction(Ability3Action, ETriggerEvent::Started, this, &ASlopArenaCharacter::OnAbility3);
		EnhancedInput->BindAction(UltimateAction, ETriggerEvent::Started, this, &ASlopArenaCharacter::OnUltimate);
	}
}

void ASlopArenaCharacter::PossessedBy(AController* NewController)
{
	Super::PossessedBy(NewController);
	if (AbilitySystemComponent)
	{
		AbilitySystemComponent->RefreshAbilityActorInfo();
	}
}

void ASlopArenaCharacter::OnRep_PlayerState()
{
	Super::OnRep_PlayerState();
	if (AbilitySystemComponent)
	{
		AbilitySystemComponent->RefreshAbilityActorInfo();
	}
}

void ASlopArenaCharacter::ApplyDamageWithKnockback(float Damage, FVector Knockback, AActor* DamageInstigator)
{
	UE_LOG(LogSlopArenaCombat, Log, TEXT("%s took %.1f damage with knockback (%s)"),
		*GetName(), Damage, *Knockback.ToString());

	// Apply via GAS gameplay effect
	if (AbilitySystemComponent && DamageInstigator)
	{
		// TODO: Create and apply FGameplayEffectSpec for damage
	}

	// Apply knockback velocity
	LaunchCharacter(Knockback, false, true);
}

// ~ Input callbacks (stubs)

void ASlopArenaCharacter::OnMove(const FInputActionValue& Value)
{
	CachedMoveInput = Value.Get<FVector>();
	// TODO: Route to NetworkPrediction input
}

void ASlopArenaCharacter::OnJumpStart()
{
	Jump();
}

void ASlopArenaCharacter::OnJumpEnd()
{
	StopJumping();
}

void ASlopArenaCharacter::OnDash()
{
	// TODO: Trigger dash ability via GAS
	UE_LOG(LogSlopArena, Verbose, TEXT("Dash input received"));
}

void ASlopArenaCharacter::OnLightAttack()
{
	// TODO: Trigger light attack ability via GAS
	if (AbilitySystemComponent)
	{
		AbilitySystemComponent->TryActivateAbilitiesByTag(FGameplayTagContainer(FGameplayTag::RequestGameplayTag(FName("Ability.LightAttack"))));
	}
}

void ASlopArenaCharacter::OnAbility1()
{
	if (AbilitySystemComponent)
	{
		AbilitySystemComponent->TryActivateAbilitiesByTag(FGameplayTagContainer(FGameplayTag::RequestGameplayTag(FName("Ability.Ability1"))));
	}
}

void ASlopArenaCharacter::OnAbility2()
{
	if (AbilitySystemComponent)
	{
		AbilitySystemComponent->TryActivateAbilitiesByTag(FGameplayTagContainer(FGameplayTag::RequestGameplayTag(FName("Ability.Ability2"))));
	}
}

void ASlopArenaCharacter::OnAbility3()
{
	if (AbilitySystemComponent)
	{
		AbilitySystemComponent->TryActivateAbilitiesByTag(FGameplayTagContainer(FGameplayTag::RequestGameplayTag(FName("Ability.Ability3"))));
	}
}

void ASlopArenaCharacter::OnUltimate()
{
	if (AbilitySystemComponent)
	{
		AbilitySystemComponent->TryActivateAbilitiesByTag(FGameplayTagContainer(FGameplayTag::RequestGameplayTag(FName("Ability.Ultimate"))));
	}
}

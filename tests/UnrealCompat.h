// Standalone compatibility header for testing Shared/ logic without Unreal Engine.
// When compiled in Unreal, all these types come from the engine.

#pragma once

#include <string>
#include <cmath>
#include <cstdint>
#include <vector>
#include <optional>
#include <algorithm>
#include <cassert>

using int32 = int32_t;
using uint8 = uint8_t;
using uint64 = uint64_t;

// Unreal UHT macros (no-op outside engine)
#define UCLASS(...)
#define USTRUCT(...)
#define UENUM(...)
#define UPROPERTY(...)
#define GENERATED_BODY()
#define GENERATED_USTRUCT_BODY()
#define UMETA(...)
#define UFUNCTION(...)
#define BlueprintType
#define Blueprintable
#define Category
#define Transient
#define BlueprintReadOnly
#define BlueprintReadWrite
#define EditAnywhere
#define EditDefaultsOnly
#define VisibleAnywhere
#define SLOPARENA_API

// FVector
struct FVector {
	float X = 0.0f, Y = 0.0f, Z = 0.0f;
	FVector() = default;
	FVector(float X, float Y, float Z) : X(X), Y(Y), Z(Z) {}
	static FVector ZeroVector;
	static FVector Forward;
	FVector operator+(const FVector& O) const { return {X+O.X, Y+O.Y, Z+O.Z}; }
	FVector operator-(const FVector& O) const { return {X-O.X, Y-O.Y, Z-O.Z}; }
	FVector operator*(float S) const { return {X*S, Y*S, Z*S}; }
	FVector operator/(float S) const { return {X/S, Y/S, Z/S}; }
	FVector& operator+=(const FVector& O) { X+=O.X; Y+=O.Y; Z+=O.Z; return *this; }
	FVector& operator-=(const FVector& O) { X-=O.X; Y-=O.Y; Z-=O.Z; return *this; }
	FVector& operator*=(float S) { X*=S; Y*=S; Z*=S; return *this; }
	float Dot(const FVector& O) const { return X*O.X + Y*O.Y + Z*O.Z; }
	float Length() const { return std::sqrt(X*X + Y*Y + Z*Z); }
	float SquaredLength() const { return X*X + Y*Y + Z*Z; }
	FVector GetSafeNormal(float Tolerance=1e-8f) const {
		float L = Length();
		return L > Tolerance ? *this / L : FVector(0,0,1);
	}
	FVector operator-() const { return {-X,-Y,-Z}; }
};

FVector FVector::ZeroVector{0,0,0};
FVector FVector::Forward{0,0,1};

// FVector2D
struct FVector2D {
	float X = 0.0f, Y = 0.0f;
	FVector2D() = default;
	FVector2D(float X, float Y) : X(X), Y(Y) {}
	explicit FVector2D(float V) : X(V), Y(V) {}
	FVector2D operator+(const FVector2D& O) const { return {X+O.X, Y+O.Y}; }
	FVector2D operator-(const FVector2D& O) const { return {X-O.X, Y-O.Y}; }
	FVector2D operator*(float S) const { return {X*S, Y*S}; }
	FVector2D operator/(float S) const { return {X/S, Y/S}; }
	FVector2D& operator/=(float S) { X/=S; Y/=S; return *this; }
	FVector2D& operator*=(float S) { X*=S; Y*=S; return *this; }
	FVector2D& operator+=(const FVector2D& O) { X+=O.X; Y+=O.Y; return *this; }
	float Length() const { return std::sqrt(X*X + Y*Y); }
	float SquaredLength() const { return X*X + Y*Y; }
};

// FMath
struct FMath {
	static float Sin(float V) { return std::sin(V); }
	static float Cos(float V) { return std::cos(V); }
	static float Acos(float V) { return std::acos(std::clamp(V, -1.0f, 1.0f)); }
	static float Sqrt(float V) { return std::sqrt(V); }
	static float Clamp(float V, float Min, float Max) { return std::clamp(V, Min, Max); }
	static int32 Clamp(int32 V, int32 Min, int32 Max) { return std::clamp(V, Min, Max); }
	static float RandRange(float Min, float Max) { return Min + (Max-Min) * (float(rand())/RAND_MAX); }
};

// TArrayView (minimal)
template<typename T>
struct TArrayView {
	const T* Data;
	int32 Count;
	TArrayView(const T* Data, int32 Count) : Data(Data), Count(Count) {}
	const T& operator[](int32 I) const { return Data[I]; }
	auto begin() const { return Data; }
	auto end() const { return Data + Count; }
};

// TOptional
template<typename T>
using TOptional = std::optional<T>;

// FString
struct FString : std::string {
	using std::string::string;
	FString() = default;
	FString(const char* S) : std::string(S) {}
	FString(const std::string& S) : std::string(S) {}
	bool IsEmpty() const { return empty(); }
};

// FText
struct FText {
	std::string Data;
	FText() = default;
	FText(const char* S) : Data(S) {}
	FText(const std::string& S) : Data(S) {}
	FString ToString() const { return FString(Data); }
	static FText FromString(const FString& S) { FText T; T.Data = S; return T; }
};

#define NSLOCTEXT(InNamespace, InKey, InText) FText(InText)

// FName
struct FName {
	std::string Data;
	FName() = default;
	FName(const char* S) : Data(S) {}
	FName(const std::string& S) : Data(S) {}
};

#define TEXT(x) x

// TMap (minimal)
template<typename K, typename V>
struct TMap {
	std::vector<std::pair<K, V>> Data;
	int32 Num() const { return (int32)Data.size(); }
	void Add(const K& Key, const V& Val) { Data.push_back({Key, Val}); }
	bool Contains(const K& Key) const { for (auto& P : Data) if (P.first == Key) return true; return false; }
	V& operator[](const K& Key) { for (auto& P : Data) if (P.first == Key) return P.second; Data.push_back({Key, V{}}); return Data.back().second; }
	void Remove(const K& Key) { for (size_t i = 0; i < Data.size(); i++) if (Data[i].first == Key) { Data.erase(Data.begin() + i); return; } }
	void Empty() { Data.clear(); }
};

// TArray (minimal std::vector wrapper)
template<typename T>
struct TArray {
	std::vector<T> Data;
	TArray() = default;
	TArray(std::initializer_list<T> L) : Data(L) {}
	int32 Num() const { return (int32)Data.size(); }
	void Add(const T& V) { Data.push_back(V); }
	T& operator[](int32 I) { return Data[I]; }
	const T& operator[](int32 I) const { return Data[I]; }
	auto begin() { return Data.begin(); }
	auto end() { return Data.end(); }
	auto begin() const { return Data.begin(); }
	auto end() const { return Data.end(); }
	void Clear() { Data.clear(); }
};

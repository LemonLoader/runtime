﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace System.Numerics.Tensors
{
    public static partial class TensorPrimitives
    {
        /// <summary>
        /// Copies <paramref name="source"/> to <paramref name="destination"/>, converting each <see cref="float" />
        /// value to its nearest representable half-precision floating-point value.
        /// </summary>
        /// <param name="source">The source span from which to copy values.</param>
        /// <param name="destination">The destination span into which the converted values should be written.</param>
        /// <exception cref="ArgumentException">Destination is too short.</exception>
        /// <remarks>
        /// <para>
        /// This method effectively computes <c><paramref name="destination" />[i] = (Half)<paramref name="source" />[i]</c>.
        /// </para>
        /// </remarks>
        public static void ConvertToHalf(ReadOnlySpan<float> source, Span<Half> destination)
        {
            if (source.Length > destination.Length)
            {
                ThrowHelper.ThrowArgument_DestinationTooShort();
            }

            for (int i = 0; i < source.Length; i++)
            {
                destination[i] = (Half)source[i];
            }
        }

        /// <summary>
        /// Copies <paramref name="source"/> to <paramref name="destination"/>, converting each half-precision
        /// floating-point value to its nearest representable <see cref="float"/> value.
        /// </summary>
        /// <param name="source">The source span from which to copy values.</param>
        /// <param name="destination">The destination span into which the converted values should be written.</param>
        /// <exception cref="ArgumentException">Destination is too short.</exception>
        /// <remarks>
        /// <para>
        /// This method effectively computes <c><paramref name="destination" />[i] = (float)<paramref name="source" />[i]</c>.
        /// </para>
        /// </remarks>
        public static void ConvertToSingle(ReadOnlySpan<Half> source, Span<float> destination)
        {
            if (source.Length > destination.Length)
            {
                ThrowHelper.ThrowArgument_DestinationTooShort();
            }

            for (int i = 0; i < source.Length; i++)
            {
                destination[i] = (float)source[i];
            }
        }

        private static float CosineSimilarityCore(ReadOnlySpan<float> x, ReadOnlySpan<float> y)
        {
            // Compute the same as:
            // TensorPrimitives.Dot(x, y) / (Math.Sqrt(TensorPrimitives.SumOfSquares(x)) * Math.Sqrt(TensorPrimitives.SumOfSquares(y)))
            // but only looping over each span once.

#if NET8_0_OR_GREATER
            if (Vector512.IsHardwareAccelerated && x.Length >= Vector512<float>.Count)
            {
                ref float xRef = ref MemoryMarshal.GetReference(x);
                ref float yRef = ref MemoryMarshal.GetReference(y);

                Vector512<float> dotProductVector = Vector512<float>.Zero;
                Vector512<float> xSumOfSquaresVector = Vector512<float>.Zero;
                Vector512<float> ySumOfSquaresVector = Vector512<float>.Zero;

                // Process vectors, summing their dot products and squares, as long as there's a vector's worth remaining.
                int oneVectorFromEnd = x.Length - Vector512<float>.Count;
                int i = 0;
                do
                {
                    Vector512<float> xVec = Vector512.LoadUnsafe(ref xRef, (uint)i);
                    Vector512<float> yVec = Vector512.LoadUnsafe(ref yRef, (uint)i);

                    dotProductVector = FusedMultiplyAdd(xVec, yVec, dotProductVector);
                    xSumOfSquaresVector = FusedMultiplyAdd(xVec, xVec, xSumOfSquaresVector);
                    ySumOfSquaresVector = FusedMultiplyAdd(yVec, yVec, ySumOfSquaresVector);

                    i += Vector512<float>.Count;
                }
                while (i <= oneVectorFromEnd);

                // Process the last vector in the span, masking off elements already processed.
                if (i != x.Length)
                {
                    Vector512<float> xVec = Vector512.LoadUnsafe(ref xRef, (uint)(x.Length - Vector512<float>.Count));
                    Vector512<float> yVec = Vector512.LoadUnsafe(ref yRef, (uint)(x.Length - Vector512<float>.Count));

                    Vector512<float> remainderMask = LoadRemainderMaskSingleVector512(x.Length - i);
                    xVec &= remainderMask;
                    yVec &= remainderMask;

                    dotProductVector = FusedMultiplyAdd(xVec, yVec, dotProductVector);
                    xSumOfSquaresVector = FusedMultiplyAdd(xVec, xVec, xSumOfSquaresVector);
                    ySumOfSquaresVector = FusedMultiplyAdd(yVec, yVec, ySumOfSquaresVector);
                }

                // Sum(X * Y) / (|X| * |Y|)
                return
                    Vector512.Sum(dotProductVector) /
                    (MathF.Sqrt(Vector512.Sum(xSumOfSquaresVector)) * MathF.Sqrt(Vector512.Sum(ySumOfSquaresVector)));
            }
#endif

            if (Vector256.IsHardwareAccelerated && x.Length >= Vector256<float>.Count)
            {
                ref float xRef = ref MemoryMarshal.GetReference(x);
                ref float yRef = ref MemoryMarshal.GetReference(y);

                Vector256<float> dotProductVector = Vector256<float>.Zero;
                Vector256<float> xSumOfSquaresVector = Vector256<float>.Zero;
                Vector256<float> ySumOfSquaresVector = Vector256<float>.Zero;

                // Process vectors, summing their dot products and squares, as long as there's a vector's worth remaining.
                int oneVectorFromEnd = x.Length - Vector256<float>.Count;
                int i = 0;
                do
                {
                    Vector256<float> xVec = Vector256.LoadUnsafe(ref xRef, (uint)i);
                    Vector256<float> yVec = Vector256.LoadUnsafe(ref yRef, (uint)i);

                    dotProductVector = FusedMultiplyAdd(xVec, yVec, dotProductVector);
                    xSumOfSquaresVector = FusedMultiplyAdd(xVec, xVec, xSumOfSquaresVector);
                    ySumOfSquaresVector = FusedMultiplyAdd(yVec, yVec, ySumOfSquaresVector);

                    i += Vector256<float>.Count;
                }
                while (i <= oneVectorFromEnd);

                // Process the last vector in the span, masking off elements already processed.
                if (i != x.Length)
                {
                    Vector256<float> xVec = Vector256.LoadUnsafe(ref xRef, (uint)(x.Length - Vector256<float>.Count));
                    Vector256<float> yVec = Vector256.LoadUnsafe(ref yRef, (uint)(x.Length - Vector256<float>.Count));

                    Vector256<float> remainderMask = LoadRemainderMaskSingleVector256(x.Length - i);
                    xVec &= remainderMask;
                    yVec &= remainderMask;

                    dotProductVector = FusedMultiplyAdd(xVec, yVec, dotProductVector);
                    xSumOfSquaresVector = FusedMultiplyAdd(xVec, xVec, xSumOfSquaresVector);
                    ySumOfSquaresVector = FusedMultiplyAdd(yVec, yVec, ySumOfSquaresVector);
                }

                // Sum(X * Y) / (|X| * |Y|)
                return
                    Vector256.Sum(dotProductVector) /
                    (MathF.Sqrt(Vector256.Sum(xSumOfSquaresVector)) * MathF.Sqrt(Vector256.Sum(ySumOfSquaresVector)));
            }

            if (Vector128.IsHardwareAccelerated && x.Length >= Vector128<float>.Count)
            {
                ref float xRef = ref MemoryMarshal.GetReference(x);
                ref float yRef = ref MemoryMarshal.GetReference(y);

                Vector128<float> dotProductVector = Vector128<float>.Zero;
                Vector128<float> xSumOfSquaresVector = Vector128<float>.Zero;
                Vector128<float> ySumOfSquaresVector = Vector128<float>.Zero;

                // Process vectors, summing their dot products and squares, as long as there's a vector's worth remaining.
                int oneVectorFromEnd = x.Length - Vector128<float>.Count;
                int i = 0;
                do
                {
                    Vector128<float> xVec = Vector128.LoadUnsafe(ref xRef, (uint)i);
                    Vector128<float> yVec = Vector128.LoadUnsafe(ref yRef, (uint)i);

                    dotProductVector = FusedMultiplyAdd(xVec, yVec, dotProductVector);
                    xSumOfSquaresVector = FusedMultiplyAdd(xVec, xVec, xSumOfSquaresVector);
                    ySumOfSquaresVector = FusedMultiplyAdd(yVec, yVec, ySumOfSquaresVector);

                    i += Vector128<float>.Count;
                }
                while (i <= oneVectorFromEnd);

                // Process the last vector in the span, masking off elements already processed.
                if (i != x.Length)
                {
                    Vector128<float> xVec = Vector128.LoadUnsafe(ref xRef, (uint)(x.Length - Vector128<float>.Count));
                    Vector128<float> yVec = Vector128.LoadUnsafe(ref yRef, (uint)(x.Length - Vector128<float>.Count));

                    Vector128<float> remainderMask = LoadRemainderMaskSingleVector128(x.Length - i);
                    xVec &= remainderMask;
                    yVec &= remainderMask;

                    dotProductVector = FusedMultiplyAdd(xVec, yVec, dotProductVector);
                    xSumOfSquaresVector = FusedMultiplyAdd(xVec, xVec, xSumOfSquaresVector);
                    ySumOfSquaresVector = FusedMultiplyAdd(yVec, yVec, ySumOfSquaresVector);
                }

                // Sum(X * Y) / (|X| * |Y|)
                return
                    Vector128.Sum(dotProductVector) /
                    (MathF.Sqrt(Vector128.Sum(xSumOfSquaresVector)) * MathF.Sqrt(Vector128.Sum(ySumOfSquaresVector)));
            }

            // Vectorization isn't supported or there are too few elements to vectorize.
            // Use a scalar implementation.
            float dotProduct = 0f, xSumOfSquares = 0f, ySumOfSquares = 0f;
            for (int i = 0; i < x.Length; i++)
            {
                dotProduct = MathF.FusedMultiplyAdd(x[i], y[i], dotProduct);
                xSumOfSquares = MathF.FusedMultiplyAdd(x[i], x[i], xSumOfSquares);
                ySumOfSquares = MathF.FusedMultiplyAdd(y[i], y[i], ySumOfSquares);
            }

            // Sum(X * Y) / (|X| * |Y|)
            return
                dotProduct /
                (MathF.Sqrt(xSumOfSquares) * MathF.Sqrt(ySumOfSquares));
        }

        private static float Aggregate<TLoad, TAggregate>(
            ReadOnlySpan<float> x)
            where TLoad : struct, IUnaryOperator
            where TAggregate : struct, IAggregationOperator
        {
            if (x.Length == 0)
            {
                return 0;
            }

            ref float xRef = ref MemoryMarshal.GetReference(x);

#if NET8_0_OR_GREATER
            if (Vector512.IsHardwareAccelerated && x.Length >= Vector512<float>.Count)
            {
                // Load the first vector as the initial set of results
                Vector512<float> result = TLoad.Invoke(Vector512.LoadUnsafe(ref xRef, 0));
                int oneVectorFromEnd = x.Length - Vector512<float>.Count;
                int i = Vector512<float>.Count;

                // Aggregate additional vectors into the result as long as there's at
                // least one full vector left to process.
                while (i <= oneVectorFromEnd)
                {
                    result = TAggregate.Invoke(result, TLoad.Invoke(Vector512.LoadUnsafe(ref xRef, (uint)i)));
                    i += Vector512<float>.Count;
                }

                // Process the last vector in the span, masking off elements already processed.
                if (i != x.Length)
                {
                    result = TAggregate.Invoke(result,
                        Vector512.ConditionalSelect(
                            Vector512.Equals(LoadRemainderMaskSingleVector512(x.Length - i), Vector512<float>.Zero),
                            Vector512.Create(TAggregate.IdentityValue),
                            TLoad.Invoke(Vector512.LoadUnsafe(ref xRef, (uint)(x.Length - Vector512<float>.Count)))));
                }

                // Aggregate the lanes in the vector back into the scalar result
                return TAggregate.Invoke(result);
            }
#endif

            if (Vector256.IsHardwareAccelerated && x.Length >= Vector256<float>.Count)
            {
                // Load the first vector as the initial set of results
                Vector256<float> result = TLoad.Invoke(Vector256.LoadUnsafe(ref xRef, 0));
                int oneVectorFromEnd = x.Length - Vector256<float>.Count;
                int i = Vector256<float>.Count;

                // Aggregate additional vectors into the result as long as there's at
                // least one full vector left to process.
                while (i <= oneVectorFromEnd)
                {
                    result = TAggregate.Invoke(result, TLoad.Invoke(Vector256.LoadUnsafe(ref xRef, (uint)i)));
                    i += Vector256<float>.Count;
                }

                // Process the last vector in the span, masking off elements already processed.
                if (i != x.Length)
                {
                    result = TAggregate.Invoke(result,
                        Vector256.ConditionalSelect(
                            Vector256.Equals(LoadRemainderMaskSingleVector256(x.Length - i), Vector256<float>.Zero),
                            Vector256.Create(TAggregate.IdentityValue),
                            TLoad.Invoke(Vector256.LoadUnsafe(ref xRef, (uint)(x.Length - Vector256<float>.Count)))));
                }

                // Aggregate the lanes in the vector back into the scalar result
                return TAggregate.Invoke(result);
            }

            if (Vector128.IsHardwareAccelerated && x.Length >= Vector128<float>.Count)
            {
                // Load the first vector as the initial set of results
                Vector128<float> result = TLoad.Invoke(Vector128.LoadUnsafe(ref xRef, 0));
                int oneVectorFromEnd = x.Length - Vector128<float>.Count;
                int i = Vector128<float>.Count;

                // Aggregate additional vectors into the result as long as there's at
                // least one full vector left to process.
                while (i <= oneVectorFromEnd)
                {
                    result = TAggregate.Invoke(result, TLoad.Invoke(Vector128.LoadUnsafe(ref xRef, (uint)i)));
                    i += Vector128<float>.Count;
                }

                // Process the last vector in the span, masking off elements already processed.
                if (i != x.Length)
                {
                    result = TAggregate.Invoke(result,
                        Vector128.ConditionalSelect(
                            Vector128.Equals(LoadRemainderMaskSingleVector128(x.Length - i), Vector128<float>.Zero),
                            Vector128.Create(TAggregate.IdentityValue),
                            TLoad.Invoke(Vector128.LoadUnsafe(ref xRef, (uint)(x.Length - Vector128<float>.Count)))));
                }

                // Aggregate the lanes in the vector back into the scalar result
                return TAggregate.Invoke(result);
            }

            // Vectorization isn't supported or there are too few elements to vectorize.
            // Use a scalar implementation.
            {
                float result = TLoad.Invoke(x[0]);
                for (int i = 1; i < x.Length; i++)
                {
                    result = TAggregate.Invoke(result, TLoad.Invoke(x[i]));
                }

                return result;
            }
        }

        private static float Aggregate<TBinary, TAggregate>(
            ReadOnlySpan<float> x, ReadOnlySpan<float> y)
            where TBinary : struct, IBinaryOperator
            where TAggregate : struct, IAggregationOperator
        {
            Debug.Assert(x.Length == y.Length);

            if (x.IsEmpty)
            {
                return 0;
            }

            ref float xRef = ref MemoryMarshal.GetReference(x);
            ref float yRef = ref MemoryMarshal.GetReference(y);

#if NET8_0_OR_GREATER
            if (Vector512.IsHardwareAccelerated && x.Length >= Vector512<float>.Count)
            {
                // Load the first vector as the initial set of results
                Vector512<float> result = TBinary.Invoke(Vector512.LoadUnsafe(ref xRef, 0), Vector512.LoadUnsafe(ref yRef, 0));
                int oneVectorFromEnd = x.Length - Vector512<float>.Count;
                int i = Vector512<float>.Count;

                // Aggregate additional vectors into the result as long as there's at
                // least one full vector left to process.
                while (i <= oneVectorFromEnd)
                {
                    result = TAggregate.Invoke(result, TBinary.Invoke(Vector512.LoadUnsafe(ref xRef, (uint)i), Vector512.LoadUnsafe(ref yRef, (uint)i)));
                    i += Vector512<float>.Count;
                }

                // Process the last vector in the spans, masking off elements already processed.
                if (i != x.Length)
                {
                    result = TAggregate.Invoke(result,
                        Vector512.ConditionalSelect(
                            Vector512.Equals(LoadRemainderMaskSingleVector512(x.Length - i), Vector512<float>.Zero),
                            Vector512.Create(TAggregate.IdentityValue),
                            TBinary.Invoke(
                                Vector512.LoadUnsafe(ref xRef, (uint)(x.Length - Vector512<float>.Count)),
                                Vector512.LoadUnsafe(ref yRef, (uint)(x.Length - Vector512<float>.Count)))));
                }

                // Aggregate the lanes in the vector back into the scalar result
                return TAggregate.Invoke(result);
            }
#endif

            if (Vector256.IsHardwareAccelerated && x.Length >= Vector256<float>.Count)
            {
                // Load the first vector as the initial set of results
                Vector256<float> result = TBinary.Invoke(Vector256.LoadUnsafe(ref xRef, 0), Vector256.LoadUnsafe(ref yRef, 0));
                int oneVectorFromEnd = x.Length - Vector256<float>.Count;
                int i = Vector256<float>.Count;

                // Aggregate additional vectors into the result as long as there's at
                // least one full vector left to process.
                while (i <= oneVectorFromEnd)
                {
                    result = TAggregate.Invoke(result, TBinary.Invoke(Vector256.LoadUnsafe(ref xRef, (uint)i), Vector256.LoadUnsafe(ref yRef, (uint)i)));
                    i += Vector256<float>.Count;
                }

                // Process the last vector in the spans, masking off elements already processed.
                if (i != x.Length)
                {
                    result = TAggregate.Invoke(result,
                        Vector256.ConditionalSelect(
                            Vector256.Equals(LoadRemainderMaskSingleVector256(x.Length - i), Vector256<float>.Zero),
                            Vector256.Create(TAggregate.IdentityValue),
                            TBinary.Invoke(
                                Vector256.LoadUnsafe(ref xRef, (uint)(x.Length - Vector256<float>.Count)),
                                Vector256.LoadUnsafe(ref yRef, (uint)(x.Length - Vector256<float>.Count)))));
                }

                // Aggregate the lanes in the vector back into the scalar result
                return TAggregate.Invoke(result);
            }

            if (Vector128.IsHardwareAccelerated && x.Length >= Vector128<float>.Count)
            {
                // Load the first vector as the initial set of results
                Vector128<float> result = TBinary.Invoke(Vector128.LoadUnsafe(ref xRef, 0), Vector128.LoadUnsafe(ref yRef, 0));
                int oneVectorFromEnd = x.Length - Vector128<float>.Count;
                int i = Vector128<float>.Count;

                // Aggregate additional vectors into the result as long as there's at
                // least one full vector left to process.
                while (i <= oneVectorFromEnd)
                {
                    result = TAggregate.Invoke(result, TBinary.Invoke(Vector128.LoadUnsafe(ref xRef, (uint)i), Vector128.LoadUnsafe(ref yRef, (uint)i)));
                    i += Vector128<float>.Count;
                }

                // Process the last vector in the spans, masking off elements already processed.
                if (i != x.Length)
                {
                    result = TAggregate.Invoke(result,
                        Vector128.ConditionalSelect(
                            Vector128.Equals(LoadRemainderMaskSingleVector128(x.Length - i), Vector128<float>.Zero),
                            Vector128.Create(TAggregate.IdentityValue),
                            TBinary.Invoke(
                                Vector128.LoadUnsafe(ref xRef, (uint)(x.Length - Vector128<float>.Count)),
                                Vector128.LoadUnsafe(ref yRef, (uint)(x.Length - Vector128<float>.Count)))));
                }

                // Aggregate the lanes in the vector back into the scalar result
                return TAggregate.Invoke(result);
            }

            // Vectorization isn't supported or there are too few elements to vectorize.
            // Use a scalar implementation.
            {
                float result = TBinary.Invoke(xRef, yRef);
                for (int i = 1; i < x.Length; i++)
                {
                    result = TAggregate.Invoke(result,
                        TBinary.Invoke(
                            Unsafe.Add(ref xRef, i),
                            Unsafe.Add(ref yRef, i)));
                }

                return result;
            }
        }

        /// <remarks>
        /// This is the same as <see cref="Aggregate{TLoad, TAggregate}(ReadOnlySpan{float})"/>,
        /// except it early exits on NaN.
        /// </remarks>
        private static float MinMaxCore<TMinMax>(ReadOnlySpan<float> x) where TMinMax : struct, IAggregationOperator
        {
            if (x.IsEmpty)
            {
                ThrowHelper.ThrowArgument_SpansMustBeNonEmpty();
            }

            // This matches the IEEE 754:2019 `maximum`/`minimum` functions.
            // It propagates NaN inputs back to the caller and
            // otherwise returns the greater of the inputs.
            // It treats +0 as greater than -0 as per the specification.

#if NET8_0_OR_GREATER
            if (Vector512.IsHardwareAccelerated && x.Length >= Vector512<float>.Count)
            {
                ref float xRef = ref MemoryMarshal.GetReference(x);

                // Load the first vector as the initial set of results, and bail immediately
                // to scalar handling if it contains any NaNs (which don't compare equally to themselves).
                Vector512<float> result = Vector512.LoadUnsafe(ref xRef, 0), current;
                if (!Vector512.EqualsAll(result, result))
                {
                    return GetFirstNaN(result);
                }

                int oneVectorFromEnd = x.Length - Vector512<float>.Count;
                int i = Vector512<float>.Count;

                // Aggregate additional vectors into the result as long as there's at least one full vector left to process.
                while (i <= oneVectorFromEnd)
                {
                    // Load the next vector, and early exit on NaN.
                    current = Vector512.LoadUnsafe(ref xRef, (uint)i);
                    if (!Vector512.EqualsAll(current, current))
                    {
                        return GetFirstNaN(current);
                    }

                    result = TMinMax.Invoke(result, current);
                    i += Vector512<float>.Count;
                }

                // If any elements remain, handle them in one final vector.
                if (i != x.Length)
                {
                    current = Vector512.LoadUnsafe(ref xRef, (uint)(x.Length - Vector512<float>.Count));
                    if (!Vector512.EqualsAll(current, current))
                    {
                        return GetFirstNaN(current);
                    }

                    result = TMinMax.Invoke(result, current);
                }

                // Aggregate the lanes in the vector to create the final scalar result.
                return TMinMax.Invoke(result);
            }
#endif

            if (Vector256.IsHardwareAccelerated && x.Length >= Vector256<float>.Count)
            {
                ref float xRef = ref MemoryMarshal.GetReference(x);

                // Load the first vector as the initial set of results, and bail immediately
                // to scalar handling if it contains any NaNs (which don't compare equally to themselves).
                Vector256<float> result = Vector256.LoadUnsafe(ref xRef, 0), current;
                if (!Vector256.EqualsAll(result, result))
                {
                    return GetFirstNaN(result);
                }

                int oneVectorFromEnd = x.Length - Vector256<float>.Count;
                int i = Vector256<float>.Count;

                // Aggregate additional vectors into the result as long as there's at least one full vector left to process.
                while (i <= oneVectorFromEnd)
                {
                    // Load the next vector, and early exit on NaN.
                    current = Vector256.LoadUnsafe(ref xRef, (uint)i);
                    if (!Vector256.EqualsAll(current, current))
                    {
                        return GetFirstNaN(current);
                    }

                    result = TMinMax.Invoke(result, current);
                    i += Vector256<float>.Count;
                }

                // If any elements remain, handle them in one final vector.
                if (i != x.Length)
                {
                    current = Vector256.LoadUnsafe(ref xRef, (uint)(x.Length - Vector256<float>.Count));
                    if (!Vector256.EqualsAll(current, current))
                    {
                        return GetFirstNaN(current);
                    }

                    result = TMinMax.Invoke(result, current);
                }

                // Aggregate the lanes in the vector to create the final scalar result.
                return TMinMax.Invoke(result);
            }

            if (Vector128.IsHardwareAccelerated && x.Length >= Vector128<float>.Count)
            {
                ref float xRef = ref MemoryMarshal.GetReference(x);

                // Load the first vector as the initial set of results, and bail immediately
                // to scalar handling if it contains any NaNs (which don't compare equally to themselves).
                Vector128<float> result = Vector128.LoadUnsafe(ref xRef, 0), current;
                if (!Vector128.EqualsAll(result, result))
                {
                    return GetFirstNaN(result);
                }

                int oneVectorFromEnd = x.Length - Vector128<float>.Count;
                int i = Vector128<float>.Count;

                // Aggregate additional vectors into the result as long as there's at least one full vector left to process.
                while (i <= oneVectorFromEnd)
                {
                    // Load the next vector, and early exit on NaN.
                    current = Vector128.LoadUnsafe(ref xRef, (uint)i);
                    if (!Vector128.EqualsAll(current, current))
                    {
                        return GetFirstNaN(current);
                    }

                    result = TMinMax.Invoke(result, current);
                    i += Vector128<float>.Count;
                }

                // If any elements remain, handle them in one final vector.
                if (i != x.Length)
                {
                    current = Vector128.LoadUnsafe(ref xRef, (uint)(x.Length - Vector128<float>.Count));
                    if (!Vector128.EqualsAll(current, current))
                    {
                        return GetFirstNaN(current);
                    }

                    result = TMinMax.Invoke(result, current);
                }

                // Aggregate the lanes in the vector to create the final scalar result.
                return TMinMax.Invoke(result);
            }

            // Scalar path used when either vectorization is not supported or the input is too small to vectorize.
            {
                float result = x[0];
                if (float.IsNaN(result))
                {
                    return result;
                }

                for (int i = 1; i < x.Length; i++)
                {
                    float current = x[i];
                    if (float.IsNaN(current))
                    {
                        return current;
                    }

                    result = TMinMax.Invoke(result, current);
                }

                return result;
            }
        }

        private static unsafe void InvokeSpanIntoSpan<TUnaryOperator>(
            ReadOnlySpan<float> x, Span<float> destination)
            where TUnaryOperator : struct, IUnaryOperator
        {
            if (x.Length > destination.Length)
            {
                ThrowHelper.ThrowArgument_DestinationTooShort();
            }

            ref float xRef = ref MemoryMarshal.GetReference(x);
            ref float dRef = ref MemoryMarshal.GetReference(destination);
            int i = 0, oneVectorFromEnd;

#if NET8_0_OR_GREATER
            if (Vector512.IsHardwareAccelerated)
            {
                oneVectorFromEnd = x.Length - Vector512<float>.Count;
                if (i <= oneVectorFromEnd)
                {
                    // Loop handling one vector at a time.
                    do
                    {
                        TUnaryOperator.Invoke(Vector512.LoadUnsafe(ref xRef, (uint)i)).StoreUnsafe(ref dRef, (uint)i);

                        i += Vector512<float>.Count;
                    }
                    while (i <= oneVectorFromEnd);

                    // Handle any remaining elements with a final vector.
                    if (i != x.Length)
                    {
                        uint lastVectorIndex = (uint)(x.Length - Vector512<float>.Count);
                        TUnaryOperator.Invoke(Vector512.LoadUnsafe(ref xRef, lastVectorIndex)).StoreUnsafe(ref dRef, lastVectorIndex);
                    }

                    return;
                }
            }
#endif

            if (Vector256.IsHardwareAccelerated)
            {
                oneVectorFromEnd = x.Length - Vector256<float>.Count;
                if (i <= oneVectorFromEnd)
                {
                    // Loop handling one vector at a time.
                    do
                    {
                        TUnaryOperator.Invoke(Vector256.LoadUnsafe(ref xRef, (uint)i)).StoreUnsafe(ref dRef, (uint)i);

                        i += Vector256<float>.Count;
                    }
                    while (i <= oneVectorFromEnd);

                    // Handle any remaining elements with a final vector.
                    if (i != x.Length)
                    {
                        uint lastVectorIndex = (uint)(x.Length - Vector256<float>.Count);
                        TUnaryOperator.Invoke(Vector256.LoadUnsafe(ref xRef, lastVectorIndex)).StoreUnsafe(ref dRef, lastVectorIndex);
                    }

                    return;
                }
            }

            if (Vector128.IsHardwareAccelerated)
            {
                oneVectorFromEnd = x.Length - Vector128<float>.Count;
                if (i <= oneVectorFromEnd)
                {
                    // Loop handling one vector at a time.
                    do
                    {
                        TUnaryOperator.Invoke(Vector128.LoadUnsafe(ref xRef, (uint)i)).StoreUnsafe(ref dRef, (uint)i);

                        i += Vector128<float>.Count;
                    }
                    while (i <= oneVectorFromEnd);

                    // Handle any remaining elements with a final vector.
                    if (i != x.Length)
                    {
                        uint lastVectorIndex = (uint)(x.Length - Vector128<float>.Count);
                        TUnaryOperator.Invoke(Vector128.LoadUnsafe(ref xRef, lastVectorIndex)).StoreUnsafe(ref dRef, lastVectorIndex);
                    }

                    return;
                }
            }

            while (i < x.Length)
            {
                Unsafe.Add(ref dRef, i) = TUnaryOperator.Invoke(Unsafe.Add(ref xRef, i));

                i++;
            }
        }

        private static unsafe void InvokeSpanSpanIntoSpan<TBinaryOperator>(
            ReadOnlySpan<float> x, ReadOnlySpan<float> y, Span<float> destination)
            where TBinaryOperator : struct, IBinaryOperator
        {
            if (x.Length != y.Length)
            {
                ThrowHelper.ThrowArgument_SpansMustHaveSameLength();
            }

            if (x.Length > destination.Length)
            {
                ThrowHelper.ThrowArgument_DestinationTooShort();
            }

            ref float xRef = ref MemoryMarshal.GetReference(x);
            ref float yRef = ref MemoryMarshal.GetReference(y);
            ref float dRef = ref MemoryMarshal.GetReference(destination);
            int i = 0, oneVectorFromEnd;

#if NET8_0_OR_GREATER
            if (Vector512.IsHardwareAccelerated)
            {
                oneVectorFromEnd = x.Length - Vector512<float>.Count;
                if (i <= oneVectorFromEnd)
                {
                    // Loop handling one vector at a time.
                    do
                    {
                        TBinaryOperator.Invoke(Vector512.LoadUnsafe(ref xRef, (uint)i),
                                               Vector512.LoadUnsafe(ref yRef, (uint)i)).StoreUnsafe(ref dRef, (uint)i);

                        i += Vector512<float>.Count;
                    }
                    while (i <= oneVectorFromEnd);

                    // Handle any remaining elements with a final vector.
                    if (i != x.Length)
                    {
                        uint lastVectorIndex = (uint)(x.Length - Vector512<float>.Count);
                        TBinaryOperator.Invoke(Vector512.LoadUnsafe(ref xRef, lastVectorIndex),
                                               Vector512.LoadUnsafe(ref yRef, lastVectorIndex)).StoreUnsafe(ref dRef, lastVectorIndex);
                    }

                    return;
                }
            }
#endif

            if (Vector256.IsHardwareAccelerated)
            {
                oneVectorFromEnd = x.Length - Vector256<float>.Count;
                if (i <= oneVectorFromEnd)
                {
                    // Loop handling one vector at a time.
                    do
                    {
                        TBinaryOperator.Invoke(Vector256.LoadUnsafe(ref xRef, (uint)i),
                                               Vector256.LoadUnsafe(ref yRef, (uint)i)).StoreUnsafe(ref dRef, (uint)i);

                        i += Vector256<float>.Count;
                    }
                    while (i <= oneVectorFromEnd);

                    // Handle any remaining elements with a final vector.
                    if (i != x.Length)
                    {
                        uint lastVectorIndex = (uint)(x.Length - Vector256<float>.Count);
                        TBinaryOperator.Invoke(Vector256.LoadUnsafe(ref xRef, lastVectorIndex),
                                               Vector256.LoadUnsafe(ref yRef, lastVectorIndex)).StoreUnsafe(ref dRef, lastVectorIndex);
                    }

                    return;
                }
            }

            if (Vector128.IsHardwareAccelerated)
            {
                oneVectorFromEnd = x.Length - Vector128<float>.Count;
                if (i <= oneVectorFromEnd)
                {
                    // Loop handling one vector at a time.
                    do
                    {
                        TBinaryOperator.Invoke(Vector128.LoadUnsafe(ref xRef, (uint)i),
                                               Vector128.LoadUnsafe(ref yRef, (uint)i)).StoreUnsafe(ref dRef, (uint)i);

                        i += Vector128<float>.Count;
                    }
                    while (i <= oneVectorFromEnd);

                    // Handle any remaining elements with a final vector.
                    if (i != x.Length)
                    {
                        uint lastVectorIndex = (uint)(x.Length - Vector128<float>.Count);
                        TBinaryOperator.Invoke(Vector128.LoadUnsafe(ref xRef, lastVectorIndex),
                                               Vector128.LoadUnsafe(ref yRef, lastVectorIndex)).StoreUnsafe(ref dRef, lastVectorIndex);
                    }

                    return;
                }
            }

            while (i < x.Length)
            {
                Unsafe.Add(ref dRef, i) = TBinaryOperator.Invoke(Unsafe.Add(ref xRef, i),
                                                                 Unsafe.Add(ref yRef, i));

                i++;
            }
        }

        private static unsafe void InvokeSpanScalarIntoSpan<TBinaryOperator>(
            ReadOnlySpan<float> x, float y, Span<float> destination)
            where TBinaryOperator : struct, IBinaryOperator
        {
            if (x.Length > destination.Length)
            {
                ThrowHelper.ThrowArgument_DestinationTooShort();
            }

            ref float xRef = ref MemoryMarshal.GetReference(x);
            ref float dRef = ref MemoryMarshal.GetReference(destination);
            int i = 0, oneVectorFromEnd;

#if NET8_0_OR_GREATER
            if (Vector512.IsHardwareAccelerated)
            {
                oneVectorFromEnd = x.Length - Vector512<float>.Count;
                if (i <= oneVectorFromEnd)
                {
                    Vector512<float> yVec = Vector512.Create(y);

                    // Loop handling one vector at a time.
                    do
                    {
                        TBinaryOperator.Invoke(Vector512.LoadUnsafe(ref xRef, (uint)i),
                                               yVec).StoreUnsafe(ref dRef, (uint)i);

                        i += Vector512<float>.Count;
                    }
                    while (i <= oneVectorFromEnd);

                    // Handle any remaining elements with a final vector.
                    if (i != x.Length)
                    {
                        uint lastVectorIndex = (uint)(x.Length - Vector512<float>.Count);
                        TBinaryOperator.Invoke(Vector512.LoadUnsafe(ref xRef, lastVectorIndex),
                                               yVec).StoreUnsafe(ref dRef, lastVectorIndex);
                    }

                    return;
                }
            }
#endif

            if (Vector256.IsHardwareAccelerated)
            {
                oneVectorFromEnd = x.Length - Vector256<float>.Count;
                if (i <= oneVectorFromEnd)
                {
                    Vector256<float> yVec = Vector256.Create(y);

                    // Loop handling one vector at a time.
                    do
                    {
                        TBinaryOperator.Invoke(Vector256.LoadUnsafe(ref xRef, (uint)i),
                                               yVec).StoreUnsafe(ref dRef, (uint)i);

                        i += Vector256<float>.Count;
                    }
                    while (i <= oneVectorFromEnd);

                    // Handle any remaining elements with a final vector.
                    if (i != x.Length)
                    {
                        uint lastVectorIndex = (uint)(x.Length - Vector256<float>.Count);
                        TBinaryOperator.Invoke(Vector256.LoadUnsafe(ref xRef, lastVectorIndex),
                                               yVec).StoreUnsafe(ref dRef, lastVectorIndex);
                    }

                    return;
                }
            }

            if (Vector128.IsHardwareAccelerated)
            {
                oneVectorFromEnd = x.Length - Vector128<float>.Count;
                if (i <= oneVectorFromEnd)
                {
                    Vector128<float> yVec = Vector128.Create(y);

                    // Loop handling one vector at a time.
                    do
                    {
                        TBinaryOperator.Invoke(Vector128.LoadUnsafe(ref xRef, (uint)i),
                                               yVec).StoreUnsafe(ref dRef, (uint)i);

                        i += Vector128<float>.Count;
                    }
                    while (i <= oneVectorFromEnd);

                    // Handle any remaining elements with a final vector.
                    if (i != x.Length)
                    {
                        uint lastVectorIndex = (uint)(x.Length - Vector128<float>.Count);
                        TBinaryOperator.Invoke(Vector128.LoadUnsafe(ref xRef, lastVectorIndex),
                                               yVec).StoreUnsafe(ref dRef, lastVectorIndex);
                    }

                    return;
                }
            }

            while (i < x.Length)
            {
                Unsafe.Add(ref dRef, i) = TBinaryOperator.Invoke(Unsafe.Add(ref xRef, i),
                                                                 y);

                i++;
            }
        }

        private static unsafe void InvokeSpanSpanSpanIntoSpan<TTernaryOperator>(
            ReadOnlySpan<float> x, ReadOnlySpan<float> y, ReadOnlySpan<float> z, Span<float> destination)
            where TTernaryOperator : struct, ITernaryOperator
        {
            if (x.Length != y.Length || x.Length != z.Length)
            {
                ThrowHelper.ThrowArgument_SpansMustHaveSameLength();
            }

            if (x.Length > destination.Length)
            {
                ThrowHelper.ThrowArgument_DestinationTooShort();
            }

            ref float xRef = ref MemoryMarshal.GetReference(x);
            ref float yRef = ref MemoryMarshal.GetReference(y);
            ref float zRef = ref MemoryMarshal.GetReference(z);
            ref float dRef = ref MemoryMarshal.GetReference(destination);
            int i = 0, oneVectorFromEnd;

#if NET8_0_OR_GREATER
            if (Vector512.IsHardwareAccelerated)
            {
                oneVectorFromEnd = x.Length - Vector512<float>.Count;
                if (i <= oneVectorFromEnd)
                {
                    // Loop handling one vector at a time.
                    do
                    {
                        TTernaryOperator.Invoke(Vector512.LoadUnsafe(ref xRef, (uint)i),
                                                Vector512.LoadUnsafe(ref yRef, (uint)i),
                                                Vector512.LoadUnsafe(ref zRef, (uint)i)).StoreUnsafe(ref dRef, (uint)i);

                        i += Vector512<float>.Count;
                    }
                    while (i <= oneVectorFromEnd);

                    // Handle any remaining elements with a final vector.
                    if (i != x.Length)
                    {
                        uint lastVectorIndex = (uint)(x.Length - Vector512<float>.Count);
                        TTernaryOperator.Invoke(Vector512.LoadUnsafe(ref xRef, lastVectorIndex),
                                                Vector512.LoadUnsafe(ref yRef, lastVectorIndex),
                                                Vector512.LoadUnsafe(ref zRef, lastVectorIndex)).StoreUnsafe(ref dRef, lastVectorIndex);
                    }

                    return;
                }
            }
#endif

            if (Vector256.IsHardwareAccelerated)
            {
                oneVectorFromEnd = x.Length - Vector256<float>.Count;
                if (i <= oneVectorFromEnd)
                {
                    // Loop handling one vector at a time.
                    do
                    {
                        TTernaryOperator.Invoke(Vector256.LoadUnsafe(ref xRef, (uint)i),
                                                Vector256.LoadUnsafe(ref yRef, (uint)i),
                                                Vector256.LoadUnsafe(ref zRef, (uint)i)).StoreUnsafe(ref dRef, (uint)i);

                        i += Vector256<float>.Count;
                    }
                    while (i <= oneVectorFromEnd);

                    // Handle any remaining elements with a final vector.
                    if (i != x.Length)
                    {
                        uint lastVectorIndex = (uint)(x.Length - Vector256<float>.Count);
                        TTernaryOperator.Invoke(Vector256.LoadUnsafe(ref xRef, lastVectorIndex),
                                                Vector256.LoadUnsafe(ref yRef, lastVectorIndex),
                                                Vector256.LoadUnsafe(ref zRef, lastVectorIndex)).StoreUnsafe(ref dRef, lastVectorIndex);
                    }

                    return;
                }
            }

            if (Vector128.IsHardwareAccelerated)
            {
                oneVectorFromEnd = x.Length - Vector128<float>.Count;
                if (i <= oneVectorFromEnd)
                {
                    // Loop handling one vector at a time.
                    do
                    {
                        TTernaryOperator.Invoke(Vector128.LoadUnsafe(ref xRef, (uint)i),
                                                Vector128.LoadUnsafe(ref yRef, (uint)i),
                                                Vector128.LoadUnsafe(ref zRef, (uint)i)).StoreUnsafe(ref dRef, (uint)i);

                        i += Vector128<float>.Count;
                    }
                    while (i <= oneVectorFromEnd);

                    // Handle any remaining elements with a final vector.
                    if (i != x.Length)
                    {
                        uint lastVectorIndex = (uint)(x.Length - Vector128<float>.Count);
                        TTernaryOperator.Invoke(Vector128.LoadUnsafe(ref xRef, lastVectorIndex),
                                                Vector128.LoadUnsafe(ref yRef, lastVectorIndex),
                                                Vector128.LoadUnsafe(ref zRef, lastVectorIndex)).StoreUnsafe(ref dRef, lastVectorIndex);
                    }

                    return;
                }
            }

            while (i < x.Length)
            {
                Unsafe.Add(ref dRef, i) = TTernaryOperator.Invoke(Unsafe.Add(ref xRef, i),
                                                                  Unsafe.Add(ref yRef, i),
                                                                  Unsafe.Add(ref zRef, i));

                i++;
            }
        }

        private static unsafe void InvokeSpanSpanScalarIntoSpan<TTernaryOperator>(
            ReadOnlySpan<float> x, ReadOnlySpan<float> y, float z, Span<float> destination)
            where TTernaryOperator : struct, ITernaryOperator
        {
            if (x.Length != y.Length)
            {
                ThrowHelper.ThrowArgument_SpansMustHaveSameLength();
            }

            if (x.Length > destination.Length)
            {
                ThrowHelper.ThrowArgument_DestinationTooShort();
            }

            ref float xRef = ref MemoryMarshal.GetReference(x);
            ref float yRef = ref MemoryMarshal.GetReference(y);
            ref float dRef = ref MemoryMarshal.GetReference(destination);
            int i = 0, oneVectorFromEnd;

#if NET8_0_OR_GREATER
            if (Vector512.IsHardwareAccelerated)
            {
                oneVectorFromEnd = x.Length - Vector512<float>.Count;
                if (i <= oneVectorFromEnd)
                {
                    Vector512<float> zVec = Vector512.Create(z);

                    // Loop handling one vector at a time.
                    do
                    {
                        TTernaryOperator.Invoke(Vector512.LoadUnsafe(ref xRef, (uint)i),
                                                Vector512.LoadUnsafe(ref yRef, (uint)i),
                                                zVec).StoreUnsafe(ref dRef, (uint)i);

                        i += Vector512<float>.Count;
                    }
                    while (i <= oneVectorFromEnd);

                    // Handle any remaining elements with a final vector.
                    if (i != x.Length)
                    {
                        uint lastVectorIndex = (uint)(x.Length - Vector512<float>.Count);
                        TTernaryOperator.Invoke(Vector512.LoadUnsafe(ref xRef, lastVectorIndex),
                                                Vector512.LoadUnsafe(ref yRef, lastVectorIndex),
                                                zVec).StoreUnsafe(ref dRef, lastVectorIndex);
                    }

                    return;
                }
            }
#endif

            if (Vector256.IsHardwareAccelerated)
            {
                oneVectorFromEnd = x.Length - Vector256<float>.Count;
                if (i <= oneVectorFromEnd)
                {
                    Vector256<float> zVec = Vector256.Create(z);

                    // Loop handling one vector at a time.
                    do
                    {
                        TTernaryOperator.Invoke(Vector256.LoadUnsafe(ref xRef, (uint)i),
                                                Vector256.LoadUnsafe(ref yRef, (uint)i),
                                                zVec).StoreUnsafe(ref dRef, (uint)i);

                        i += Vector256<float>.Count;
                    }
                    while (i <= oneVectorFromEnd);

                    // Handle any remaining elements with a final vector.
                    if (i != x.Length)
                    {
                        uint lastVectorIndex = (uint)(x.Length - Vector256<float>.Count);
                        TTernaryOperator.Invoke(Vector256.LoadUnsafe(ref xRef, lastVectorIndex),
                                                Vector256.LoadUnsafe(ref yRef, lastVectorIndex),
                                                zVec).StoreUnsafe(ref dRef, lastVectorIndex);
                    }

                    return;
                }
            }

            if (Vector128.IsHardwareAccelerated)
            {
                oneVectorFromEnd = x.Length - Vector128<float>.Count;
                if (i <= oneVectorFromEnd)
                {
                    Vector128<float> zVec = Vector128.Create(z);

                    // Loop handling one vector at a time.
                    do
                    {
                        TTernaryOperator.Invoke(Vector128.LoadUnsafe(ref xRef, (uint)i),
                                                Vector128.LoadUnsafe(ref yRef, (uint)i),
                                                zVec).StoreUnsafe(ref dRef, (uint)i);

                        i += Vector128<float>.Count;
                    }
                    while (i <= oneVectorFromEnd);

                    // Handle any remaining elements with a final vector.
                    if (i != x.Length)
                    {
                        uint lastVectorIndex = (uint)(x.Length - Vector128<float>.Count);
                        TTernaryOperator.Invoke(Vector128.LoadUnsafe(ref xRef, lastVectorIndex),
                                                Vector128.LoadUnsafe(ref yRef, lastVectorIndex),
                                                zVec).StoreUnsafe(ref dRef, lastVectorIndex);
                    }

                    return;
                }
            }

            while (i < x.Length)
            {
                Unsafe.Add(ref dRef, i) = TTernaryOperator.Invoke(Unsafe.Add(ref xRef, i),
                                                                  Unsafe.Add(ref yRef, i),
                                                                  z);

                i++;
            }
        }

        private static unsafe void InvokeSpanScalarSpanIntoSpan<TTernaryOperator>(
            ReadOnlySpan<float> x, float y, ReadOnlySpan<float> z, Span<float> destination)
            where TTernaryOperator : struct, ITernaryOperator
        {
            if (x.Length != z.Length)
            {
                ThrowHelper.ThrowArgument_SpansMustHaveSameLength();
            }

            if (x.Length > destination.Length)
            {
                ThrowHelper.ThrowArgument_DestinationTooShort();
            }

            ref float xRef = ref MemoryMarshal.GetReference(x);
            ref float zRef = ref MemoryMarshal.GetReference(z);
            ref float dRef = ref MemoryMarshal.GetReference(destination);
            int i = 0, oneVectorFromEnd;

#if NET8_0_OR_GREATER
            if (Vector512.IsHardwareAccelerated)
            {
                oneVectorFromEnd = x.Length - Vector512<float>.Count;
                if (i <= oneVectorFromEnd)
                {
                    Vector512<float> yVec = Vector512.Create(y);

                    // Loop handling one vector at a time.
                    do
                    {
                        TTernaryOperator.Invoke(Vector512.LoadUnsafe(ref xRef, (uint)i),
                                                yVec,
                                                Vector512.LoadUnsafe(ref zRef, (uint)i)).StoreUnsafe(ref dRef, (uint)i);

                        i += Vector512<float>.Count;
                    }
                    while (i <= oneVectorFromEnd);

                    // Handle any remaining elements with a final vector.
                    if (i != x.Length)
                    {
                        uint lastVectorIndex = (uint)(x.Length - Vector512<float>.Count);
                        TTernaryOperator.Invoke(Vector512.LoadUnsafe(ref xRef, lastVectorIndex),
                                                yVec,
                                                Vector512.LoadUnsafe(ref zRef, lastVectorIndex)).StoreUnsafe(ref dRef, lastVectorIndex);
                    }

                    return;
                }
            }
#endif

            if (Vector256.IsHardwareAccelerated)
            {
                oneVectorFromEnd = x.Length - Vector256<float>.Count;
                if (i <= oneVectorFromEnd)
                {
                    Vector256<float> yVec = Vector256.Create(y);

                    // Loop handling one vector at a time.
                    do
                    {
                        TTernaryOperator.Invoke(Vector256.LoadUnsafe(ref xRef, (uint)i),
                                                yVec,
                                                Vector256.LoadUnsafe(ref zRef, (uint)i)).StoreUnsafe(ref dRef, (uint)i);

                        i += Vector256<float>.Count;
                    }
                    while (i <= oneVectorFromEnd);

                    // Handle any remaining elements with a final vector.
                    if (i != x.Length)
                    {
                        uint lastVectorIndex = (uint)(x.Length - Vector256<float>.Count);
                        TTernaryOperator.Invoke(Vector256.LoadUnsafe(ref xRef, lastVectorIndex),
                                                yVec,
                                                Vector256.LoadUnsafe(ref zRef, lastVectorIndex)).StoreUnsafe(ref dRef, lastVectorIndex);
                    }

                    return;
                }
            }

            if (Vector128.IsHardwareAccelerated)
            {
                oneVectorFromEnd = x.Length - Vector128<float>.Count;
                if (i <= oneVectorFromEnd)
                {
                    Vector128<float> yVec = Vector128.Create(y);

                    // Loop handling one vector at a time.
                    do
                    {
                        TTernaryOperator.Invoke(Vector128.LoadUnsafe(ref xRef, (uint)i),
                                                yVec,
                                                Vector128.LoadUnsafe(ref zRef, (uint)i)).StoreUnsafe(ref dRef, (uint)i);

                        i += Vector128<float>.Count;
                    }
                    while (i <= oneVectorFromEnd);

                    // Handle any remaining elements with a final vector.
                    if (i != x.Length)
                    {
                        uint lastVectorIndex = (uint)(x.Length - Vector128<float>.Count);
                        TTernaryOperator.Invoke(Vector128.LoadUnsafe(ref xRef, lastVectorIndex),
                                                yVec,
                                                Vector128.LoadUnsafe(ref zRef, lastVectorIndex)).StoreUnsafe(ref dRef, lastVectorIndex);
                    }

                    return;
                }
            }

            while (i < x.Length)
            {
                Unsafe.Add(ref dRef, i) = TTernaryOperator.Invoke(Unsafe.Add(ref xRef, i),
                                                                  y,
                                                                  Unsafe.Add(ref zRef, i));

                i++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<float> FusedMultiplyAdd(Vector128<float> x, Vector128<float> y, Vector128<float> addend)
        {
            if (Fma.IsSupported)
            {
                return Fma.MultiplyAdd(x, y, addend);
            }

            if (AdvSimd.IsSupported)
            {
                return AdvSimd.FusedMultiplyAdd(addend, x, y);
            }

            return (x * y) + addend;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector256<float> FusedMultiplyAdd(Vector256<float> x, Vector256<float> y, Vector256<float> addend)
        {
            if (Fma.IsSupported)
            {
                return Fma.MultiplyAdd(x, y, addend);
            }

            return (x * y) + addend;
        }

#if NET8_0_OR_GREATER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector512<float> FusedMultiplyAdd(Vector512<float> x, Vector512<float> y, Vector512<float> addend)
        {
            if (Avx512F.IsSupported)
            {
                return Avx512F.FusedMultiplyAdd(x, y, addend);
            }

            return (x * y) + addend;
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float HorizontalAggregate<TAggregate>(Vector128<float> x) where TAggregate : struct, IBinaryOperator =>
            TAggregate.Invoke(
                TAggregate.Invoke(x[0], x[1]),
                TAggregate.Invoke(x[2], x[3]));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float HorizontalAggregate<TAggregate>(Vector256<float> x) where TAggregate : struct, IBinaryOperator =>
            HorizontalAggregate<TAggregate>(TAggregate.Invoke(x.GetLower(), x.GetUpper()));

#if NET8_0_OR_GREATER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float HorizontalAggregate<TAggregate>(Vector512<float> x) where TAggregate : struct, IBinaryOperator =>
            HorizontalAggregate<TAggregate>(TAggregate.Invoke(x.GetLower(), x.GetUpper()));
#endif

        private static bool IsNegative(float f) => float.IsNegative(f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<float> IsNegative(Vector128<float> vector) =>
            Vector128.LessThan(vector.AsInt32(), Vector128<int>.Zero).AsSingle();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector256<float> IsNegative(Vector256<float> vector) =>
            Vector256.LessThan(vector.AsInt32(), Vector256<int>.Zero).AsSingle();

#if NET8_0_OR_GREATER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector512<float> IsNegative(Vector512<float> vector) =>
            Vector512.LessThan(vector.AsInt32(), Vector512<int>.Zero).AsSingle();
#endif

        private static float GetFirstNaN(Vector128<float> vector) =>
            vector[BitOperations.TrailingZeroCount((~Vector128.Equals(vector, vector)).ExtractMostSignificantBits())];

        private static float GetFirstNaN(Vector256<float> vector) =>
            vector[BitOperations.TrailingZeroCount((~Vector256.Equals(vector, vector)).ExtractMostSignificantBits())];

#if NET8_0_OR_GREATER
        private static float GetFirstNaN(Vector512<float> vector) =>
            vector[BitOperations.TrailingZeroCount((~Vector512.Equals(vector, vector)).ExtractMostSignificantBits())];
#endif

        private static float Log2(float x) => MathF.Log2(x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe Vector128<float> LoadRemainderMaskSingleVector128(int validItems) =>
            Vector128.LoadUnsafe(
                ref Unsafe.As<uint, float>(ref MemoryMarshal.GetReference(RemainderUInt32Mask_16x16)),
                (uint)((validItems * 16) + 12)); // last four floats in the row

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe Vector256<float> LoadRemainderMaskSingleVector256(int validItems) =>
            Vector256.LoadUnsafe(
                ref Unsafe.As<uint, float>(ref MemoryMarshal.GetReference(RemainderUInt32Mask_16x16)),
                (uint)((validItems * 16) + 8)); // last eight floats in the row

#if NET8_0_OR_GREATER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe Vector512<float> LoadRemainderMaskSingleVector512(int validItems) =>
            Vector512.LoadUnsafe(
                ref Unsafe.As<uint, float>(ref MemoryMarshal.GetReference(RemainderUInt32Mask_16x16)),
                (uint)(validItems * 16)); // all sixteen floats in the row
#endif

        private readonly struct AddOperator : IAggregationOperator
        {
            public static float Invoke(float x, float y) => x + y;
            public static Vector128<float> Invoke(Vector128<float> x, Vector128<float> y) => x + y;
            public static Vector256<float> Invoke(Vector256<float> x, Vector256<float> y) => x + y;
#if NET8_0_OR_GREATER
            public static Vector512<float> Invoke(Vector512<float> x, Vector512<float> y) => x + y;
#endif

            public static float Invoke(Vector128<float> x) => Vector128.Sum(x);
            public static float Invoke(Vector256<float> x) => Vector256.Sum(x);
#if NET8_0_OR_GREATER
            public static float Invoke(Vector512<float> x) => Vector512.Sum(x);
#endif

            public static float IdentityValue => 0;
        }

        private readonly struct SubtractOperator : IBinaryOperator
        {
            public static float Invoke(float x, float y) => x - y;
            public static Vector128<float> Invoke(Vector128<float> x, Vector128<float> y) => x - y;
            public static Vector256<float> Invoke(Vector256<float> x, Vector256<float> y) => x - y;
#if NET8_0_OR_GREATER
            public static Vector512<float> Invoke(Vector512<float> x, Vector512<float> y) => x - y;
#endif
        }

        private readonly struct SubtractSquaredOperator : IBinaryOperator
        {
            public static float Invoke(float x, float y)
            {
                float tmp = x - y;
                return tmp * tmp;
            }

            public static Vector128<float> Invoke(Vector128<float> x, Vector128<float> y)
            {
                Vector128<float> tmp = x - y;
                return tmp * tmp;
            }

            public static Vector256<float> Invoke(Vector256<float> x, Vector256<float> y)
            {
                Vector256<float> tmp = x - y;
                return tmp * tmp;
            }

#if NET8_0_OR_GREATER
            public static Vector512<float> Invoke(Vector512<float> x, Vector512<float> y)
            {
                Vector512<float> tmp = x - y;
                return tmp * tmp;
            }
#endif
        }

        private readonly struct MultiplyOperator : IAggregationOperator
        {
            public static float Invoke(float x, float y) => x * y;
            public static Vector128<float> Invoke(Vector128<float> x, Vector128<float> y) => x * y;
            public static Vector256<float> Invoke(Vector256<float> x, Vector256<float> y) => x * y;
#if NET8_0_OR_GREATER
            public static Vector512<float> Invoke(Vector512<float> x, Vector512<float> y) => x * y;
#endif

            public static float Invoke(Vector128<float> x) => HorizontalAggregate<MultiplyOperator>(x);
            public static float Invoke(Vector256<float> x) => HorizontalAggregate<MultiplyOperator>(x);
#if NET8_0_OR_GREATER
            public static float Invoke(Vector512<float> x) => HorizontalAggregate<MultiplyOperator>(x);
#endif

            public static float IdentityValue => 1;
        }

        private readonly struct DivideOperator : IBinaryOperator
        {
            public static float Invoke(float x, float y) => x / y;
            public static Vector128<float> Invoke(Vector128<float> x, Vector128<float> y) => x / y;
            public static Vector256<float> Invoke(Vector256<float> x, Vector256<float> y) => x / y;
#if NET8_0_OR_GREATER
            public static Vector512<float> Invoke(Vector512<float> x, Vector512<float> y) => x / y;
#endif
        }

        private readonly struct MaxOperator : IAggregationOperator
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float Invoke(float x, float y) =>
                x == y ?
                    (IsNegative(x) ? y : x) :
                    (y > x ? y : x);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector128<float> Invoke(Vector128<float> x, Vector128<float> y)
            {
                if (AdvSimd.IsSupported)
                {
                    return AdvSimd.Max(x, y);
                }

                return
                    Vector128.ConditionalSelect(Vector128.Equals(x, y),
                        Vector128.ConditionalSelect(IsNegative(x), y, x),
                        Vector128.Max(x, y));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector256<float> Invoke(Vector256<float> x, Vector256<float> y) =>
                Vector256.ConditionalSelect(Vector256.Equals(x, y),
                    Vector256.ConditionalSelect(IsNegative(x), y, x),
                    Vector256.Max(x, y));

#if NET8_0_OR_GREATER
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector512<float> Invoke(Vector512<float> x, Vector512<float> y) =>
                Vector512.ConditionalSelect(Vector512.Equals(x, y),
                    Vector512.ConditionalSelect(IsNegative(x), y, x),
                    Vector512.Max(x, y));
#endif

            public static float Invoke(Vector128<float> x) => HorizontalAggregate<MaxOperator>(x);
            public static float Invoke(Vector256<float> x) => HorizontalAggregate<MaxOperator>(x);
#if NET8_0_OR_GREATER
            public static float Invoke(Vector512<float> x) => HorizontalAggregate<MaxOperator>(x);
#endif
        }

        private readonly struct MaxPropagateNaNOperator : IBinaryOperator
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float Invoke(float x, float y) => MathF.Max(x, y);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector128<float> Invoke(Vector128<float> x, Vector128<float> y)
            {
                if (AdvSimd.IsSupported)
                {
                    return AdvSimd.Max(x, y);
                }

                return
                    Vector128.ConditionalSelect(Vector128.Equals(x, x),
                        Vector128.ConditionalSelect(Vector128.Equals(y, y),
                            Vector128.ConditionalSelect(Vector128.Equals(x, y),
                                Vector128.ConditionalSelect(IsNegative(x), y, x),
                                Vector128.Max(x, y)),
                            y),
                        x);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector256<float> Invoke(Vector256<float> x, Vector256<float> y) =>
                Vector256.ConditionalSelect(Vector256.Equals(x, x),
                    Vector256.ConditionalSelect(Vector256.Equals(y, y),
                        Vector256.ConditionalSelect(Vector256.Equals(x, y),
                            Vector256.ConditionalSelect(IsNegative(x), y, x),
                            Vector256.Max(x, y)),
                        y),
                    x);

#if NET8_0_OR_GREATER
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector512<float> Invoke(Vector512<float> x, Vector512<float> y) =>
                Vector512.ConditionalSelect(Vector512.Equals(x, x),
                    Vector512.ConditionalSelect(Vector512.Equals(y, y),
                        Vector512.ConditionalSelect(Vector512.Equals(x, y),
                            Vector512.ConditionalSelect(IsNegative(x), y, x),
                            Vector512.Max(x, y)),
                        y),
                    x);
#endif
        }

        private readonly struct MaxMagnitudeOperator : IAggregationOperator
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float Invoke(float x, float y)
            {
                float xMag = MathF.Abs(x), yMag = MathF.Abs(y);
                return
                    xMag == yMag ?
                        (IsNegative(x) ? y : x) :
                        (xMag > yMag ? x : y);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector128<float> Invoke(Vector128<float> x, Vector128<float> y)
            {
                Vector128<float> xMag = Vector128.Abs(x), yMag = Vector128.Abs(y);
                return
                    Vector128.ConditionalSelect(Vector128.Equals(xMag, yMag),
                        Vector128.ConditionalSelect(IsNegative(x), y, x),
                        Vector128.ConditionalSelect(Vector128.GreaterThan(xMag, yMag), x, y));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector256<float> Invoke(Vector256<float> x, Vector256<float> y)
            {
                Vector256<float> xMag = Vector256.Abs(x), yMag = Vector256.Abs(y);
                return
                    Vector256.ConditionalSelect(Vector256.Equals(xMag, yMag),
                        Vector256.ConditionalSelect(IsNegative(x), y, x),
                        Vector256.ConditionalSelect(Vector256.GreaterThan(xMag, yMag), x, y));
            }

#if NET8_0_OR_GREATER
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector512<float> Invoke(Vector512<float> x, Vector512<float> y)
            {
                Vector512<float> xMag = Vector512.Abs(x), yMag = Vector512.Abs(y);
                return
                    Vector512.ConditionalSelect(Vector512.Equals(xMag, yMag),
                        Vector512.ConditionalSelect(IsNegative(x), y, x),
                        Vector512.ConditionalSelect(Vector512.GreaterThan(xMag, yMag), x, y));
            }
#endif

            public static float Invoke(Vector128<float> x) => HorizontalAggregate<MaxMagnitudeOperator>(x);
            public static float Invoke(Vector256<float> x) => HorizontalAggregate<MaxMagnitudeOperator>(x);
#if NET8_0_OR_GREATER
            public static float Invoke(Vector512<float> x) => HorizontalAggregate<MaxMagnitudeOperator>(x);
#endif
        }

        private readonly struct MaxMagnitudePropagateNaNOperator : IBinaryOperator
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float Invoke(float x, float y) => MathF.MaxMagnitude(x, y);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector128<float> Invoke(Vector128<float> x, Vector128<float> y)
            {
                Vector128<float> xMag = Vector128.Abs(x), yMag = Vector128.Abs(y);
                return
                    Vector128.ConditionalSelect(Vector128.Equals(x, x),
                        Vector128.ConditionalSelect(Vector128.Equals(y, y),
                            Vector128.ConditionalSelect(Vector128.Equals(yMag, xMag),
                                Vector128.ConditionalSelect(IsNegative(x), y, x),
                                Vector128.ConditionalSelect(Vector128.GreaterThan(yMag, xMag), y, x)),
                            y),
                        x);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector256<float> Invoke(Vector256<float> x, Vector256<float> y)
            {
                Vector256<float> xMag = Vector256.Abs(x), yMag = Vector256.Abs(y);
                return
                    Vector256.ConditionalSelect(Vector256.Equals(x, x),
                        Vector256.ConditionalSelect(Vector256.Equals(y, y),
                            Vector256.ConditionalSelect(Vector256.Equals(xMag, yMag),
                                Vector256.ConditionalSelect(IsNegative(x), y, x),
                                Vector256.ConditionalSelect(Vector256.GreaterThan(xMag, yMag), x, y)),
                            y),
                        x);
            }

#if NET8_0_OR_GREATER
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector512<float> Invoke(Vector512<float> x, Vector512<float> y)
            {
                Vector512<float> xMag = Vector512.Abs(x), yMag = Vector512.Abs(y);
                return
                    Vector512.ConditionalSelect(Vector512.Equals(x, x),
                        Vector512.ConditionalSelect(Vector512.Equals(y, y),
                            Vector512.ConditionalSelect(Vector512.Equals(xMag, yMag),
                                Vector512.ConditionalSelect(IsNegative(x), y, x),
                                Vector512.ConditionalSelect(Vector512.GreaterThan(xMag, yMag), x, y)),
                            y),
                        x);
            }
#endif
        }

        private readonly struct MinOperator : IAggregationOperator
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float Invoke(float x, float y) =>
                x == y ?
                    (IsNegative(y) ? y : x) :
                    (y < x ? y : x);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector128<float> Invoke(Vector128<float> x, Vector128<float> y)
            {
                if (AdvSimd.IsSupported)
                {
                    return AdvSimd.Min(x, y);
                }

                return
                    Vector128.ConditionalSelect(Vector128.Equals(x, y),
                        Vector128.ConditionalSelect(IsNegative(y), y, x),
                        Vector128.Min(x, y));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector256<float> Invoke(Vector256<float> x, Vector256<float> y) =>
                Vector256.ConditionalSelect(Vector256.Equals(x, y),
                    Vector256.ConditionalSelect(IsNegative(y), y, x),
                    Vector256.Min(x, y));

#if NET8_0_OR_GREATER
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector512<float> Invoke(Vector512<float> x, Vector512<float> y) =>
                Vector512.ConditionalSelect(Vector512.Equals(x, y),
                    Vector512.ConditionalSelect(IsNegative(y), y, x),
                    Vector512.Min(x, y));
#endif

            public static float Invoke(Vector128<float> x) => HorizontalAggregate<MinOperator>(x);
            public static float Invoke(Vector256<float> x) => HorizontalAggregate<MinOperator>(x);
#if NET8_0_OR_GREATER
            public static float Invoke(Vector512<float> x) => HorizontalAggregate<MinOperator>(x);
#endif
        }

        private readonly struct MinPropagateNaNOperator : IBinaryOperator
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float Invoke(float x, float y) => MathF.Min(x, y);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector128<float> Invoke(Vector128<float> x, Vector128<float> y)
            {
                if (AdvSimd.IsSupported)
                {
                    return AdvSimd.Min(x, y);
                }

                return
                    Vector128.ConditionalSelect(Vector128.Equals(x, x),
                        Vector128.ConditionalSelect(Vector128.Equals(y, y),
                            Vector128.ConditionalSelect(Vector128.Equals(x, y),
                                Vector128.ConditionalSelect(IsNegative(x), x, y),
                                Vector128.Min(x, y)),
                            y),
                        x);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector256<float> Invoke(Vector256<float> x, Vector256<float> y) =>
                Vector256.ConditionalSelect(Vector256.Equals(x, x),
                    Vector256.ConditionalSelect(Vector256.Equals(y, y),
                        Vector256.ConditionalSelect(Vector256.Equals(x, y),
                            Vector256.ConditionalSelect(IsNegative(x), x, y),
                            Vector256.Min(x, y)),
                        y),
                    x);

#if NET8_0_OR_GREATER
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector512<float> Invoke(Vector512<float> x, Vector512<float> y) =>
                Vector512.ConditionalSelect(Vector512.Equals(x, x),
                    Vector512.ConditionalSelect(Vector512.Equals(y, y),
                        Vector512.ConditionalSelect(Vector512.Equals(x, y),
                            Vector512.ConditionalSelect(IsNegative(x), x, y),
                            Vector512.Min(x, y)),
                        y),
                    x);
#endif
        }

        private readonly struct MinMagnitudeOperator : IAggregationOperator
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float Invoke(float x, float y)
            {
                float xMag = MathF.Abs(x), yMag = MathF.Abs(y);
                return xMag == yMag ?
                    (IsNegative(y) ? y : x) :
                    (yMag < xMag ? y : x);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector128<float> Invoke(Vector128<float> x, Vector128<float> y)
            {
                Vector128<float> xMag = Vector128.Abs(x), yMag = Vector128.Abs(y);
                return
                    Vector128.ConditionalSelect(Vector128.Equals(yMag, xMag),
                        Vector128.ConditionalSelect(IsNegative(y), y, x),
                        Vector128.ConditionalSelect(Vector128.LessThan(yMag, xMag), y, x));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector256<float> Invoke(Vector256<float> x, Vector256<float> y)
            {
                Vector256<float> xMag = Vector256.Abs(x), yMag = Vector256.Abs(y);
                return
                    Vector256.ConditionalSelect(Vector256.Equals(yMag, xMag),
                        Vector256.ConditionalSelect(IsNegative(y), y, x),
                        Vector256.ConditionalSelect(Vector256.LessThan(yMag, xMag), y, x));
            }

#if NET8_0_OR_GREATER
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector512<float> Invoke(Vector512<float> x, Vector512<float> y)
            {
                Vector512<float> xMag = Vector512.Abs(x), yMag = Vector512.Abs(y);
                return
                    Vector512.ConditionalSelect(Vector512.Equals(yMag, xMag),
                        Vector512.ConditionalSelect(IsNegative(y), y, x),
                        Vector512.ConditionalSelect(Vector512.LessThan(yMag, xMag), y, x));
            }
#endif

            public static float Invoke(Vector128<float> x) => HorizontalAggregate<MinMagnitudeOperator>(x);
            public static float Invoke(Vector256<float> x) => HorizontalAggregate<MinMagnitudeOperator>(x);
#if NET8_0_OR_GREATER
            public static float Invoke(Vector512<float> x) => HorizontalAggregate<MinMagnitudeOperator>(x);
#endif
        }

        private readonly struct MinMagnitudePropagateNaNOperator : IBinaryOperator
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float Invoke(float x, float y) => MathF.MinMagnitude(x, y);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector128<float> Invoke(Vector128<float> x, Vector128<float> y)
            {
                Vector128<float> xMag = Vector128.Abs(x), yMag = Vector128.Abs(y);
                return
                    Vector128.ConditionalSelect(Vector128.Equals(x, x),
                        Vector128.ConditionalSelect(Vector128.Equals(y, y),
                            Vector128.ConditionalSelect(Vector128.Equals(yMag, xMag),
                                Vector128.ConditionalSelect(IsNegative(x), x, y),
                                Vector128.ConditionalSelect(Vector128.LessThan(xMag, yMag), x, y)),
                            y),
                        x);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector256<float> Invoke(Vector256<float> x, Vector256<float> y)
            {
                Vector256<float> xMag = Vector256.Abs(x), yMag = Vector256.Abs(y);
                return
                    Vector256.ConditionalSelect(Vector256.Equals(x, x),
                        Vector256.ConditionalSelect(Vector256.Equals(y, y),
                            Vector256.ConditionalSelect(Vector256.Equals(yMag, xMag),
                                Vector256.ConditionalSelect(IsNegative(x), x, y),
                                Vector256.ConditionalSelect(Vector256.LessThan(xMag, yMag), x, y)),
                            y),
                        x);
            }

#if NET8_0_OR_GREATER
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector512<float> Invoke(Vector512<float> x, Vector512<float> y)
            {
                Vector512<float> xMag = Vector512.Abs(x), yMag = Vector512.Abs(y);
                return
                    Vector512.ConditionalSelect(Vector512.Equals(x, x),
                        Vector512.ConditionalSelect(Vector512.Equals(y, y),
                            Vector512.ConditionalSelect(Vector512.Equals(yMag, xMag),
                                Vector512.ConditionalSelect(IsNegative(x), x, y),
                                Vector512.ConditionalSelect(Vector512.LessThan(xMag, yMag), x, y)),
                            y),
                        x);
            }
#endif
        }

        private readonly struct NegateOperator : IUnaryOperator
        {
            public static float Invoke(float x) => -x;
            public static Vector128<float> Invoke(Vector128<float> x) => -x;
            public static Vector256<float> Invoke(Vector256<float> x) => -x;
#if NET8_0_OR_GREATER
            public static Vector512<float> Invoke(Vector512<float> x) => -x;
#endif
        }

        private readonly struct AddMultiplyOperator : ITernaryOperator
        {
            public static float Invoke(float x, float y, float z) => (x + y) * z;
            public static Vector128<float> Invoke(Vector128<float> x, Vector128<float> y, Vector128<float> z) => (x + y) * z;
            public static Vector256<float> Invoke(Vector256<float> x, Vector256<float> y, Vector256<float> z) => (x + y) * z;
#if NET8_0_OR_GREATER
            public static Vector512<float> Invoke(Vector512<float> x, Vector512<float> y, Vector512<float> z) => (x + y) * z;
#endif
        }

        private readonly struct MultiplyAddOperator : ITernaryOperator
        {
            public static float Invoke(float x, float y, float z) => (x * y) + z;
            public static Vector128<float> Invoke(Vector128<float> x, Vector128<float> y, Vector128<float> z) => (x * y) + z;
            public static Vector256<float> Invoke(Vector256<float> x, Vector256<float> y, Vector256<float> z) => (x * y) + z;
#if NET8_0_OR_GREATER
            public static Vector512<float> Invoke(Vector512<float> x, Vector512<float> y, Vector512<float> z) => (x * y) + z;
#endif
        }

        private readonly struct IdentityOperator : IUnaryOperator
        {
            public static float Invoke(float x) => x;
            public static Vector128<float> Invoke(Vector128<float> x) => x;
            public static Vector256<float> Invoke(Vector256<float> x) => x;
#if NET8_0_OR_GREATER
            public static Vector512<float> Invoke(Vector512<float> x) => x;
#endif
        }

        private readonly struct SquaredOperator : IUnaryOperator
        {
            public static float Invoke(float x) => x * x;
            public static Vector128<float> Invoke(Vector128<float> x) => x * x;
            public static Vector256<float> Invoke(Vector256<float> x) => x * x;
#if NET8_0_OR_GREATER
            public static Vector512<float> Invoke(Vector512<float> x) => x * x;
#endif
        }

        private readonly struct AbsoluteOperator : IUnaryOperator
        {
            public static float Invoke(float x) => MathF.Abs(x);
            public static Vector128<float> Invoke(Vector128<float> x) => Vector128.Abs(x);
            public static Vector256<float> Invoke(Vector256<float> x) => Vector256.Abs(x);
#if NET8_0_OR_GREATER
            public static Vector512<float> Invoke(Vector512<float> x) => Vector512.Abs(x);
#endif
        }

        private interface IUnaryOperator
        {
            static abstract float Invoke(float x);
            static abstract Vector128<float> Invoke(Vector128<float> x);
            static abstract Vector256<float> Invoke(Vector256<float> x);
#if NET8_0_OR_GREATER
            static abstract Vector512<float> Invoke(Vector512<float> x);
#endif
        }

        private interface IBinaryOperator
        {
            static abstract float Invoke(float x, float y);
            static abstract Vector128<float> Invoke(Vector128<float> x, Vector128<float> y);
            static abstract Vector256<float> Invoke(Vector256<float> x, Vector256<float> y);
#if NET8_0_OR_GREATER
            static abstract Vector512<float> Invoke(Vector512<float> x, Vector512<float> y);
#endif
        }

        private interface IAggregationOperator : IBinaryOperator
        {
            static abstract float Invoke(Vector128<float> x);
            static abstract float Invoke(Vector256<float> x);
#if NET8_0_OR_GREATER
            static abstract float Invoke(Vector512<float> x);
#endif

            static virtual float IdentityValue => throw new NotSupportedException();
        }

        private interface ITernaryOperator
        {
            static abstract float Invoke(float x, float y, float z);
            static abstract Vector128<float> Invoke(Vector128<float> x, Vector128<float> y, Vector128<float> z);
            static abstract Vector256<float> Invoke(Vector256<float> x, Vector256<float> y, Vector256<float> z);
#if NET8_0_OR_GREATER
            static abstract Vector512<float> Invoke(Vector512<float> x, Vector512<float> y, Vector512<float> z);
#endif
        }
    }
}

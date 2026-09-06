using System;
using System.Threading;

namespace fNbt {
    /// <summary> Settings for SNBT (stringified NBT) text. Members named <c>Write*</c> affect
    /// <see cref="NbtTag.ToSnbt(SnbtOptions)"/> only, members named <c>Read*</c> would affect
    /// parsing only, and unprefixed members both. Every call reads the values it needs when it
    /// starts, so changing an instance later does not affect a call in progress. The static
    /// <c>Default*</c> properties seed new instances and the overloads that take no options;
    /// configure those during startup. </summary>
    public sealed class SnbtOptions {
        static int defaultWriteLayout = (int)SnbtLayout.Compact;

        /// <summary> Initial <see cref="WriteLayout"/> for new options and for
        /// <see cref="NbtTag.ToSnbt()"/>. Initially <see cref="SnbtLayout.Compact"/>. </summary>
        /// <exception cref="ArgumentOutOfRangeException"> value is not a defined layout. </exception>
        public static SnbtLayout DefaultWriteLayout {
            get { return (SnbtLayout)Volatile.Read(ref defaultWriteLayout); }
            set {
                ValidateLayout(value);
                Volatile.Write(ref defaultWriteLayout, (int)value);
            }
        }


        /// <summary> Creates options initialized from the current defaults. Later changes to the
        /// defaults do not affect this instance. </summary>
        public SnbtOptions() {
            writeLayout = DefaultWriteLayout;
        }


        /// <summary> Layout of the text <see cref="NbtTag.ToSnbt(SnbtOptions)"/> produces.
        /// Initialized from <see cref="DefaultWriteLayout"/>. </summary>
        /// <exception cref="ArgumentOutOfRangeException"> value is not a defined layout. </exception>
        public SnbtLayout WriteLayout {
            get { return writeLayout; }
            set {
                ValidateLayout(value);
                writeLayout = value;
            }
        }

        SnbtLayout writeLayout;


        static void ValidateLayout(SnbtLayout value) {
            if (value < SnbtLayout.Compact || value > SnbtLayout.Indented) {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Not a defined SnbtLayout.");
            }
        }
    }
}

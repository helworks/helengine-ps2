#include "platform/ps2/rendering/Ps2RenderProxy.hpp"

#include "Entity.hpp"
#include "IDrawable3D.hpp"
#include "platform/ps2/rendering/Ps2RuntimeMaterial.hpp"
#include "platform/ps2/rendering/Ps2RuntimeModel.hpp"
#include "runtime/native_cast.hpp"

namespace helengine::ps2 {
    Ps2RenderProxy::Ps2RenderProxy()
        : Drawable(nullptr),
          Material(nullptr),
          Model(nullptr),
          Static(false) {
    }

    ::IDrawable3D* Ps2RenderProxy::GetDrawable() const {
        return Drawable;
    }

    Ps2RuntimeMaterial* Ps2RenderProxy::GetMaterial() const {
        return Material;
    }

    Ps2RuntimeModel* Ps2RenderProxy::GetModel() const {
        return Model;
    }

    bool Ps2RenderProxy::IsStatic() const {
        return Static;
    }

    void Ps2RenderProxy::Synchronize(::IDrawable3D* drawable) {
        Drawable = drawable;
        Material = nullptr;
        Model = nullptr;
        Static = false;

        if (drawable == nullptr) {
            return;
        }

        ::Entity* parent = drawable->get_Parent();
        if (parent == nullptr || parent->get_IsDisposed()) {
            return;
        }

        Model = he_cpp_try_cast<Ps2RuntimeModel>(drawable->get_Model());
        Array<::RuntimeMaterial*>* materials = drawable->get_Materials();
        ::RuntimeMaterial* runtimeMaterial = materials != nullptr && materials->Length > 0
            ? (*materials)[0]
            : nullptr;
        Material = he_cpp_try_cast<Ps2RuntimeMaterial>(runtimeMaterial);
        Static = parent->get_Static();
    }
}

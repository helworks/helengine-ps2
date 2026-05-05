#pragma once

class Entity;
class IDrawable3D;

namespace helengine::ps2 {
    class Ps2RuntimeMaterial;
    class Ps2RuntimeModel;

    class Ps2RenderProxy {
    public:
        Ps2RenderProxy();

        ::IDrawable3D* GetDrawable() const;
        Ps2RuntimeMaterial* GetMaterial() const;
        Ps2RuntimeModel* GetModel() const;
        bool IsStatic() const;
        void Synchronize(::IDrawable3D* drawable);

    private:
        ::IDrawable3D* Drawable;
        Ps2RuntimeMaterial* Material;
        Ps2RuntimeModel* Model;
        bool Static;
    };
}
